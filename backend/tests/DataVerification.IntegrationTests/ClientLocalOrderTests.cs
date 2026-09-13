using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The "local order" flag on a client.
///
/// The whole of this feature is one rule: exactly one client carries it. Setting it on another
/// moves it, clearing it on the only holder is refused, and deleting that client is refused too —
/// each of those is a different way to end up with none, which is the state the platform must
/// never be in.
/// </summary>
public sealed class ClientLocalOrderTests : ApiTestBase
{
    public ClientLocalOrderTests(ApiFactory factory) : base(factory) { }

    private static string Clients => Url("/admin/clients");

    private static async Task<List<JsonElement>> ListAsync(HttpClient admin)
    {
        var response = await admin.GetAsync($"{Clients}?page=1&pageSize=200");
        await EnsureSuccessAsync(response);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();
    }

    private static async Task<JsonElement> HolderAsync(HttpClient admin) =>
        (await ListAsync(admin)).Single(c => c.GetProperty("isLocalOrder").GetBoolean());

    private static async Task<JsonElement> CreateAsync(HttpClient admin, bool isLocalOrder = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var response = await admin.PostAsJsonAsync(Clients, new
        {
            code = $"LO{suffix}",
            name = $"Local order test {suffix}",
            isActive = true,
            isLocalOrder,
        });

        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task RestoreAsync(HttpClient admin, JsonElement client)
    {
        await EnsureSuccessAsync(await admin.PostAsJsonAsync(Clients, new
        {
            id = client.GetProperty("id").GetGuid(),
            code = client.GetProperty("code").GetString(),
            name = client.GetProperty("name").GetString(),
            isActive = client.GetProperty("isActive").GetBoolean(),
            isLocalOrder = true,
        }));
    }

    [Fact]
    public async Task ExactlyOneClientIsTheLocalOne()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Seeded, so a fresh database is never without one.
        (await ListAsync(admin))
            .Count(c => c.GetProperty("isLocalOrder").GetBoolean())
            .Should().Be(1);
    }

    [Fact]
    public async Task SettingItOnAnotherClientMovesIt()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var before = await HolderAsync(admin);

        var created = await CreateAsync(admin, isLocalOrder: true);

        try
        {
            created.GetProperty("isLocalOrder").GetBoolean().Should().BeTrue();

            var holders = (await ListAsync(admin))
                .Where(c => c.GetProperty("isLocalOrder").GetBoolean())
                .ToList();

            // Moved, not added: the previous holder gave it up in the same save.
            holders.Should().HaveCount(1);
            holders[0].GetProperty("id").GetGuid().Should().Be(created.GetProperty("id").GetGuid());
        }
        finally
        {
            await RestoreAsync(admin, before);
            await admin.DeleteAsync($"{Clients}/{created.GetProperty("id").GetGuid()}");
        }
    }

    [Fact]
    public async Task ClearingItOnTheOnlyHolderIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var holder = await HolderAsync(admin);

        var response = await admin.PostAsJsonAsync(Clients, new
        {
            id = holder.GetProperty("id").GetGuid(),
            code = holder.GetProperty("code").GetString(),
            name = holder.GetProperty("name").GetString(),
            isActive = holder.GetProperty("isActive").GetBoolean(),
            isLocalOrder = false,
        });

        // Refused rather than obeyed: there is no other client to fall back to.
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("client.local_order_required");

        // And it really is still set.
        (await HolderAsync(admin)).GetProperty("id").GetGuid()
            .Should().Be(holder.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task DeletingTheLocalClientIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var before = await HolderAsync(admin);
        var created = await CreateAsync(admin, isLocalOrder: true);
        var id = created.GetProperty("id").GetGuid();

        try
        {
            var response = await admin.DeleteAsync($"{Clients}/{id}");

            // Deleting it is one more way to end up with none.
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadProblemCodeAsync(response)).Should().Be("client.local_order_required");
        }
        finally
        {
            await RestoreAsync(admin, before);
            await admin.DeleteAsync($"{Clients}/{id}");
        }
    }

    [Fact]
    public async Task SavingAClientWithoutTheFlagLeavesTheHolderAlone()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var holder = await HolderAsync(admin);
        var created = await CreateAsync(admin);

        try
        {
            created.GetProperty("isLocalOrder").GetBoolean().Should().BeFalse();

            // An ordinary edit of an ordinary client must not disturb the flag.
            (await HolderAsync(admin)).GetProperty("id").GetGuid()
                .Should().Be(holder.GetProperty("id").GetGuid());
        }
        finally
        {
            await admin.DeleteAsync($"{Clients}/{created.GetProperty("id").GetGuid()}");
        }
    }

    [Fact]
    public async Task TheFlagIsNotReadableWithoutAnAdminSession()
    {
        (await CreateClient().GetAsync(Clients)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
