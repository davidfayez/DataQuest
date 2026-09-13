using System.Net;
using System.Text;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataVerification.UnitTests.Security;

/// <summary>
/// Reading a geolocation provider's answer into the security log.
///
/// The lookup is best-effort by design: everything it can go wrong doing must end with a null and a
/// reset that still works, because a log entry missing a city is worth far more than an applicant
/// who could not get their password.
/// </summary>
public sealed class IpGeolocationReadingTests
{
    private const string FullPayload = """
        {
          "status": "success",
          "continent": "Asia",
          "continentCode": "AS",
          "country": "Uzbekistan",
          "countryCode": "UZ",
          "region": "TA",
          "regionName": "Tashkent",
          "city": "Tashkent",
          "district": "Yunusabad",
          "zip": "100000",
          "lat": 41.2995,
          "lon": 69.2401,
          "timezone": "Asia/Tashkent",
          "offset": 18000,
          "currency": "UZS",
          "isp": "Uzbektelecom",
          "org": "Uztelecom Backbone",
          "as": "AS28910 Uzbektelekom",
          "reverse": "host-84-54-inbox.uz",
          "mobile": false,
          "proxy": true,
          "hosting": false,
          "query": "84.54.0.1"
        }
        """;

    private static HttpIpGeolocator Build(HttpStatusCode status, string body) =>
        new(
            new HttpClient(new StubHandler(status, body)),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GeolocationOptions()),
            NullLogger<HttpIpGeolocator>.Instance);

    [Fact]
    public async Task ReadsEveryFieldTheProviderReturns()
    {
        var location = await Build(HttpStatusCode.OK, FullPayload).LocateAsync("84.54.0.1");

        location.Should().BeEquivalentTo(new IpLocation
        {
            Continent = "Asia",
            ContinentCode = "AS",
            Country = "Uzbekistan",
            CountryCode = "UZ",
            Region = "TA",
            RegionName = "Tashkent",
            City = "Tashkent",
            District = "Yunusabad",
            PostalCode = "100000",
            Latitude = 41.2995,
            Longitude = 69.2401,
            TimeZone = "Asia/Tashkent",
            UtcOffsetSeconds = 18000,
            Currency = "UZS",
            Isp = "Uzbektelecom",
            Organisation = "Uztelecom Backbone",
            AutonomousSystem = "AS28910 Uzbektelekom",
            ReverseDns = "host-84-54-inbox.uz",
            IsMobileNetwork = false,
            IsProxy = true,
            IsHosting = false,
        });
    }

    [Fact]
    public async Task KeepsFalseApartFromUnknown()
    {
        var location = await Build(HttpStatusCode.OK, FullPayload).LocateAsync("84.54.0.1");

        // "Not a proxy" and "nobody said" are different facts, and a bool would collapse them.
        location!.IsProxy.Should().BeTrue();
        location.IsHosting.Should().BeFalse();

        var sparse = await Build(HttpStatusCode.OK, """{"status":"success","city":"Cairo"}""")
            .LocateAsync("41.0.0.1");

        sparse!.IsProxy.Should().BeNull();
    }

    [Fact]
    public async Task TreatsAnEmptyStringAsAbsent()
    {
        // ip-api returns "" rather than omitting a field it has no value for, and an empty city
        // should read as unknown rather than as blank.
        var location = await Build(
                HttpStatusCode.OK,
                """{"status":"success","country":"Egypt","city":"","isp":"   "}""")
            .LocateAsync("41.0.0.1");

        location!.Country.Should().Be("Egypt");
        location.City.Should().BeNull();
        location.Isp.Should().BeNull();
    }

    [Fact]
    public async Task GivesNothingBackWhenTheProviderReportsFailure()
    {
        // ip-api reports its own failures in the body with a 200, so the status field decides.
        var location = await Build(
                HttpStatusCode.OK,
                """{"status":"fail","message":"reserved range"}""")
            .LocateAsync("84.54.0.1");

        location.Should().BeNull();
    }

    [Fact]
    public async Task GivesNothingBackWhenTheAnswerCarriesNothingUsable()
    {
        var location = await Build(HttpStatusCode.OK, """{"status":"success"}""")
            .LocateAsync("84.54.0.1");

        location.Should().BeNull();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.0.0.4")]
    [InlineData("192.168.1.10")]
    [InlineData("172.20.0.1")]
    [InlineData("not-an-address")]
    [InlineData(null)]
    public async Task DoesNotAskAboutAnAddressThatCannotBeSomewhere(string? ip)
    {
        // A loopback or private address is the server's own network; asking wastes a call, and on
        // a paid plan, money.
        var handler = new StubHandler(HttpStatusCode.OK, FullPayload);
        var geolocator = new HttpIpGeolocator(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GeolocationOptions()),
            NullLogger<HttpIpGeolocator>.Instance);

        (await geolocator.LocateAsync(ip)).Should().BeNull();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AsksOnlyOncePerAddress()
    {
        var handler = new StubHandler(HttpStatusCode.OK, FullPayload);
        var geolocator = new HttpIpGeolocator(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GeolocationOptions()),
            NullLogger<HttpIpGeolocator>.Instance);

        await geolocator.LocateAsync("84.54.0.1");
        await geolocator.LocateAsync("84.54.0.1");

        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task StaysQuietWhenLookupIsSwitchedOff()
    {
        var handler = new StubHandler(HttpStatusCode.OK, FullPayload);
        var geolocator = new HttpIpGeolocator(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GeolocationOptions { Enabled = false }),
            NullLogger<HttpIpGeolocator>.Instance);

        (await geolocator.LocateAsync("84.54.0.1")).Should().BeNull();
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "this is not json")]
    public async Task SwallowsAProviderThatFails(HttpStatusCode status, string body)
    {
        // Over quota, down, or answering with nonsense: the reset must not care.
        var act = async () => await Build(status, body).LocateAsync("84.54.0.1");

        (await act.Should().NotThrowAsync()).Which.Should().BeNull();
    }

    [Fact]
    public async Task AsksForEveryFieldIncludingReverseDns()
    {
        var handler = new StubHandler(HttpStatusCode.OK, FullPayload);
        var geolocator = new HttpIpGeolocator(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GeolocationOptions()),
            NullLogger<HttpIpGeolocator>.Instance);

        await geolocator.LocateAsync("84.54.0.1");

        // Reverse DNS is the closest thing to a machine name available at this distance, so a
        // default that quietly stopped asking for it would matter.
        handler.LastUrl.Should().Contain("reverse");
        handler.LastUrl.Should().Contain("isp").And.Contain("proxy").And.Contain("hosting");
        handler.LastUrl.Should().Contain("84.54.0.1");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public int Calls { get; private set; }

        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri?.ToString();

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
