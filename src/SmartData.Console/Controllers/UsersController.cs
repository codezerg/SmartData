using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Models;
using SmartData.Contracts;
using SmartData.Server;

namespace SmartData.Console.Controllers;

public class UsersController : ConsoleBaseController
{
    private readonly ConsoleRoutes _routes;

    public UsersController(IAuthenticatedProcedureService procedureService, ConsoleRoutes routes) : base(procedureService)
    {
        _routes = routes;
    }

    [HttpGet("/console/users")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = await ExecuteAsync<List<UserListItem>>("sp_user_list", ct: ct);
        var model = new UsersViewModel { Users = users };
        await PopulateLayout(null, ct);
        return PageOrPartial("Index", model);
    }

    [HttpPost("/console/users")]
    public async Task<IActionResult> Create([FromForm] string? username, [FromForm] string? password, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            await ExecuteAsync<string>("sp_user_create", new { Username = username, Password = password }, ct);

        var users = await ExecuteAsync<List<UserListItem>>("sp_user_list", ct: ct);
        var model = new UsersViewModel { Users = users };
        return PartialView("_UsersTable", model);
    }

    [HttpDelete("/console/users/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await ExecuteAsync<string>("sp_user_delete", new { UserId = id }, ct);

        var users = await ExecuteAsync<List<UserListItem>>("sp_user_list", ct: ct);
        var model = new UsersViewModel { Users = users };
        return PartialView("_UsersTable", model);
    }

    [HttpGet("/console/users/new")]
    public async Task<IActionResult> New(CancellationToken ct)
    {
        var model = new UserEditViewModel
        {
            IsNew = true,
            AllPermissions = BuildPermissionGroups([])
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Edit", model);
    }

    [HttpGet("/console/users/{id}")]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var user = await ExecuteAsync<UserGetResult>("sp_user_get", new { UserId = id }, ct);
        var model = new UserEditViewModel
        {
            Id = user.Id,
            Username = user.Username,
            IsAdmin = user.IsAdmin,
            IsDisabled = user.IsDisabled,
            CreatedAt = user.CreatedAt,
            Permissions = user.Permissions,
            AllPermissions = BuildPermissionGroups(user.Permissions)
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Edit", model);
    }

    [HttpPost("/console/users/new")]
    public async Task<IActionResult> CreateUser(
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] bool isAdmin,
        [FromForm] string[]? permissions,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Username and password are required.");

            await ExecuteAsync<string>("sp_user_create", new { Username = username, Password = password }, ct);

            // Get the newly created user to find their ID
            var users = await ExecuteAsync<List<UserListItem>>("sp_user_list", ct: ct);
            var created = users.FirstOrDefault(u => u.Username == username);
            if (created == null)
                throw new InvalidOperationException("User was created but could not be found.");

            // Update admin status if needed
            if (isAdmin)
                await ExecuteAsync<string>("sp_user_update", new { UserId = created.Id, IsAdmin = true }, ct);

            // Grant permissions
            if (!isAdmin && permissions != null)
            {
                foreach (var perm in permissions)
                    await ExecuteAsync<string>("sp_user_permission_grant", new { UserId = created.Id, PermissionKey = perm }, ct);
            }

            Response.Headers["HX-Redirect"] = _routes.Path($"users/{created.Id}");
            return Ok();
        }
        catch (Exception ex)
        {
            var model = new UserEditViewModel
            {
                IsNew = true,
                Username = username,
                IsAdmin = isAdmin,
                AllPermissions = BuildPermissionGroups(permissions?.ToList() ?? []),
                ErrorMessage = ex.Message
            };
            await PopulateLayout(null, ct);
            return PageOrPartial("Edit", model);
        }
    }

    [HttpPost("/console/users/{id}")]
    public async Task<IActionResult> Update(
        string id,
        [FromForm] string username,
        [FromForm] string? password,
        [FromForm] bool isAdmin,
        [FromForm] bool isEnabled,
        [FromForm] string[]? permissions,
        CancellationToken ct)
    {
        try
        {
            // Update user fields
            await ExecuteAsync<string>("sp_user_update", new { UserId = id, Username = username, Password = password, IsAdmin = isAdmin, IsDisabled = isEnabled }, ct);

            // Sync permissions (only if not admin — admins get all permissions implicitly)
            if (!isAdmin)
            {
                var current = await ExecuteAsync<UserPermissionListResult>("sp_user_permission_list", new { UserId = id }, ct);
                var desired = permissions?.ToHashSet() ?? [];
                var currentSet = current.Permissions.ToHashSet();

                // Revoke removed
                foreach (var perm in currentSet.Except(desired))
                    await ExecuteAsync<string>("sp_user_permission_revoke", new { UserId = id, PermissionKey = perm }, ct);

                // Grant added
                foreach (var perm in desired.Except(currentSet))
                    await ExecuteAsync<string>("sp_user_permission_grant", new { UserId = id, PermissionKey = perm }, ct);
            }

            // Reload
            var user = await ExecuteAsync<UserGetResult>("sp_user_get", new { UserId = id }, ct);
            var model = new UserEditViewModel
            {
                Id = user.Id,
                Username = user.Username,
                IsAdmin = user.IsAdmin,
                IsDisabled = user.IsDisabled,
                CreatedAt = user.CreatedAt,
                Permissions = user.Permissions,
                AllPermissions = BuildPermissionGroups(user.Permissions),
                SuccessMessage = "User updated successfully."
            };
            await PopulateLayout(null, ct);
            return PageOrPartial("Edit", model);
        }
        catch (Exception ex)
        {
            var user = await ExecuteAsync<UserGetResult>("sp_user_get", new { UserId = id }, ct);
            var model = new UserEditViewModel
            {
                Id = user.Id,
                Username = username,
                IsAdmin = isAdmin,
                IsDisabled = isEnabled,
                CreatedAt = user.CreatedAt,
                Permissions = permissions?.ToList() ?? [],
                AllPermissions = BuildPermissionGroups(permissions?.ToList() ?? []),
                ErrorMessage = ex.Message
            };
            await PopulateLayout(null, ct);
            return PageOrPartial("Edit", model);
        }
    }

    private static List<PermissionGroup> BuildPermissionGroups(List<string> granted)
    {
        var grantedSet = granted.ToHashSet();
        var groups = new List<PermissionGroup>();

        // System permissions
        foreach (var grp in Permissions.System.GroupBy(p => p.Key.Split(':')[0]))
        {
            groups.Add(new PermissionGroup
            {
                Category = grp.Key,
                Entries = grp.Select(p => new PermissionEntry
                {
                    Key = p.Key,
                    Name = p.Action == "*" ? "All" : p.Action,
                    Description = p.Description,
                    Granted = grantedSet.Contains(p.Key)
                }).ToList()
            });
        }

        // Scoped permissions (using * wildcard prefix)
        foreach (var grp in Permissions.Scoped.GroupBy(p => p.Key.Split(':')[0]))
        {
            groups.Add(new PermissionGroup
            {
                Category = $"* : {grp.Key}",
                Entries = grp.Select(p => new PermissionEntry
                {
                    Key = $"*:{p.Key}",
                    Name = p.Action == "*" ? "All" : p.Action,
                    Description = p.Description,
                    Granted = grantedSet.Contains($"*:{p.Key}")
                }).ToList()
            });
        }

        return groups;
    }
}
