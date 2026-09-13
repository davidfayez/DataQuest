using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Admin.AuditLog;
using DataVerification.Application.Features.Admin.Dashboard;
using DataVerification.Application.Features.Admin.Orders;
using DataVerification.Application.Features.Admin.Rbac;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers.Admin;

/// <summary>Dashboard, order search, RBAC administration and the audit trail.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[Produces("application/json")]
public sealed class AdminPlatformController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminPlatformController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    /// <summary>Requires the create or the update permission depending on whether this is a new row.</summary>
    private void RequireUpsert(Guid? id, string createPermission, string updatePermission)
    {
        var required = id is null || id == Guid.Empty ? createPermission : updatePermission;
        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the '{required}' permission.");
        }
    }

    // ------------------------------------------------------------ Dashboard

    /// <summary>Counters by status, revenue from the ledger, and recent activity.</summary>
    [HttpGet("dashboard")]
    [RequirePermission(Permissions.DashboardView)]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDto>> GetDashboard(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetDashboardQuery(), cancellationToken));

    // --------------------------------------------------------------- Orders

    [HttpGet("orders")]
    [RequirePermission(Permissions.OrdersView)]
    [ProducesResponseType(typeof(PagedResult<AdminOrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminOrderListItemDto>>> GetOrders(
        [FromQuery] GetOrdersQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpGet("orders/{orderId:guid}")]
    [RequirePermission(Permissions.OrdersView)]
    [ProducesResponseType(typeof(AdminOrderDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminOrderDetailsDto>> GetOrder(
        Guid orderId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetOrderDetailsQuery(orderId), cancellationToken));

    // ---------------------------------------------------------------- RBAC

    /// <summary>
    /// The permission catalogue for the role and user editors. Readable by anyone who can view
    /// roles or manage admin users.
    /// </summary>
    [HttpGet("permissions")]
    [Authorize(Policy = PolicyNames.Admin)]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PermissionGroupDto>>> GetPermissions(
        CancellationToken cancellationToken)
    {
        if (!_currentUser.Permissions.Contains(Permissions.RolesView)
            && !_currentUser.Permissions.Contains(Permissions.AdminUsersView)
            && !_currentUser.Permissions.Contains(Permissions.AdminUsersCreate)
            && !_currentUser.Permissions.Contains(Permissions.AdminUsersUpdate))
        {
            throw new ForbiddenAccessException(
                "This action requires a roles or admin-users permission.");
        }

        return Ok(await _sender.Send(new GetPermissionsQuery(), cancellationToken));
    }

    [HttpGet("roles")]
    [RequirePermission(Permissions.RolesView)]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> GetRoles(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetRolesQuery(), cancellationToken));

    [HttpPost("roles")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleDto>> UpsertRole(
        [FromBody] UpsertRoleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id, Permissions.RolesCreate, Permissions.RolesUpdate);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("roles/{id:guid}")]
    [RequirePermission(Permissions.RolesDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteRoleCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpGet("users")]
    [RequirePermission(Permissions.AdminUsersView)]
    [ProducesResponseType(typeof(PagedResult<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> GetAdminUsers(
        [FromQuery] GetAdminUsersQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost("users")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> UpsertAdminUser(
        [FromBody] UpsertAdminUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id, Permissions.AdminUsersCreate, Permissions.AdminUsersUpdate);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Activates or deactivates an admin user. Accounts are never hard-deleted.</summary>
    [HttpPost("users/{id:guid}/active")]
    [RequirePermission(Permissions.AdminUsersUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetAdminUserActive(
        Guid id,
        [FromBody] SetActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _sender.Send(new SetAdminUserActiveCommand(id, request.IsActive), cancellationToken);
        return NoContent();
    }

    // ------------------------------------------------------------ Audit log

    [HttpGet("audit-log")]
    [RequirePermission(Permissions.AuditLogView)]
    [ProducesResponseType(typeof(PagedResult<AuditLogEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditLogEntryDto>>> GetAuditLog(
        [FromQuery] GetAuditLogQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));
}

public sealed record SetActiveRequest(bool IsActive);
