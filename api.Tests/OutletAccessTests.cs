using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using Xunit;

namespace Cafe.Api.Tests;

[Collection("Auth environment")]
public sealed class OutletAccessTests
{
    private const string UserId = "111111111111111111111111";
    private const string OutletId = "222222222222222222222222";
    private const string OtherOutletId = "333333333333333333333333";
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<IStaffRepository> _staff = new(MockBehavior.Strict);
    private readonly Mock<IOutletRepository> _outlets = new(MockBehavior.Strict);
    private readonly AuthService _auth;
    private readonly User _user = new() { Id = UserId, Role = "waiter", Email = "waiter@example.com" };

    public OutletAccessTests()
    {
        var previousSecret = Environment.GetEnvironmentVariable("Jwt__Secret");
        try
        {
            Environment.SetEnvironmentVariable("Jwt__Secret", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            _auth = new AuthService();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Jwt__Secret", previousSecret);
        }
        _users.Setup(repository => repository.GetUserByIdAsync(UserId)).ReturnsAsync(_user);
        _staff.Setup(repository => repository.GetStaffByUserIdAsync(UserId)).ReturnsAsync((Staff?)null);
        _staff.Setup(repository => repository.GetStaffByEmailAsync(_user.Email)).ReturnsAsync((Staff?)null);
        _outlets.Setup(repository => repository.GetOutletByIdAsync(OutletId, false))
            .ReturnsAsync(new Outlet { Id = OutletId, IsActive = true });
    }

    [Fact]
    public async Task WaiterCanAccessOutletAssignedToLinkedStaffProfile()
    {
        LinkedStaff();
        var result = await Validate(Request("waiter"), OutletId);
        Assert.True(result.hasAccess, result.errorMessage);
        Assert.Equal(OutletId, result.outletId);
        _staff.Verify(repository => repository.GetStaffByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CookWithWaiterRoleCanUseStaffOutletAssignment()
    {
        LinkedStaff();
        var request = Request("cook", new[] { "cook", "waiter" });
        var authorization = await AuthorizationHelper.ValidateWaiterRole(request, _auth);
        Assert.True(authorization.isAuthorized);
        Assert.Equal("cook", authorization.role);
        Assert.True(RoleHelper.HasAnyRole(
            AuthorizationHelper.GetRolesFromAuthorizationHeader(request, _auth), RoleHelper.WaiterRoles));
        Assert.True((await Validate(request, OutletId)).hasAccess);
    }

    [Fact]
    public async Task LegacyUnlinkedProfileCanBeResolvedByAccountEmail()
    {
        _staff.Setup(repository => repository.GetStaffByEmailAsync(_user.Email))
            .ReturnsAsync(new Staff { Email = _user.Email.ToUpperInvariant(), OutletIds = new() { OutletId } });
        Assert.True((await Validate(Request("waiter"), OutletId)).hasAccess);
    }

    [Fact]
    public async Task AccountAssignmentStillWorksWithoutStaffProfile()
    {
        _user.AssignedOutlets.Add(OutletId);
        Assert.True((await Validate(Request("waiter"), OutletId)).hasAccess);
        _staff.Verify(repository => repository.GetStaffByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StaffAssignmentDoesNotGrantAccessToAnotherOutlet()
    {
        LinkedStaff();
        var result = await Validate(Request("waiter"), OtherOutletId);
        Assert.False(result.hasAccess);
        Assert.Contains("does not have access", result.errorMessage);
        _outlets.Verify(repository => repository.GetOutletByIdAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task InactiveStaffProfileDoesNotGrantAccess()
    {
        LinkedStaff(isActive: false);
        Assert.False((await Validate(Request("waiter"), OutletId)).hasAccess);
    }

    [Fact]
    public async Task EmailMatchCannotUseProfileLinkedToAnotherAccount()
    {
        _staff.Setup(repository => repository.GetStaffByEmailAsync(_user.Email))
            .ReturnsAsync(new Staff { UserId = "another-user", Email = _user.Email, OutletIds = new() { OutletId } });
        Assert.False((await Validate(Request("waiter"), OutletId)).hasAccess);
    }

    [Fact]
    public async Task CustomerCannotGainStaffOutletPermissions()
    {
        LinkedStaff();
        Assert.False((await Validate(Request("user"), OutletId)).hasAccess);
        _staff.Verify(repository => repository.GetStaffByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UnassignedWaiterDoesNotGainAccessJustByHavingTheRole()
    {
        Assert.False((await Validate(Request("waiter"), OutletId)).hasAccess);
    }

    [Fact]
    public async Task DefaultOutletIsContextNotPermission()
    {
        _user.DefaultOutletId = OutletId;
        Assert.False((await Validate(Request("waiter"), null)).hasAccess);
    }

    [Fact]
    public async Task StaffAssignmentCanAuthorizeDefaultOutletContext()
    {
        LinkedStaff();
        _user.DefaultOutletId = OutletId;
        var result = await Validate(Request("waiter"), null);
        Assert.True(result.hasAccess, result.errorMessage);
        Assert.Equal(OutletId, result.outletId);
    }

    [Fact]
    public async Task InactiveOutletRemainsRejectedForStaff()
    {
        LinkedStaff();
        _outlets.Setup(repository => repository.GetOutletByIdAsync(OutletId, false))
            .ReturnsAsync(new Outlet { Id = OutletId, IsActive = false, OutletName = "Closed" });
        var result = await Validate(Request("waiter"), OutletId);
        Assert.False(result.hasAccess);
        Assert.Contains("not active", result.errorMessage);
    }

    [Fact]
    public async Task MissingOutletRemainsRejected()
    {
        LinkedStaff();
        _outlets.Setup(repository => repository.GetOutletByIdAsync(OutletId, false)).ReturnsAsync((Outlet?)null);
        Assert.False((await Validate(Request("waiter"), OutletId)).hasAccess);
    }

    [Fact]
    public async Task AdminAccessRecognizesSecondaryRoleClaims()
    {
        Assert.True((await Validate(Request("waiter", new[] { "waiter", "admin" }), OutletId)).hasAccess);
        _users.Verify(repository => repository.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
        _staff.Verify(repository => repository.GetStaffByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvalidTokenRemainsRejected()
    {
        var request = Request("waiter");
        request.Headers.Remove("Authorization");
        request.Headers.Add("Authorization", "Bearer not-a-token");
        Assert.False((await Validate(request, OutletId)).hasAccess);
        _users.Verify(repository => repository.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
    }

    private void LinkedStaff(bool isActive = true) =>
        _staff.Setup(repository => repository.GetStaffByUserIdAsync(UserId))
            .ReturnsAsync(new Staff { UserId = UserId, IsActive = isActive, OutletIds = new() { OutletId } });

    private HttpRequestData Request(string role, IEnumerable<string>? additionalRoles = null)
    {
        var token = _auth.GenerateJwtToken(UserId, "waiter", role, additionalRoles: additionalRoles);
        var headers = new HttpHeadersCollection { { "Authorization", $"Bearer {token}" } };
        var request = new Mock<HttpRequestData>(Mock.Of<FunctionContext>());
        request.SetupGet(value => value.Headers).Returns(headers);
        return request.Object;
    }

    private Task<(bool hasAccess, string? outletId, string? errorMessage)> Validate(HttpRequestData request, string? outletId) =>
        OutletHelper.ValidateOutletAccess(request, _auth, _users.Object, _staff.Object, _outlets.Object, outletId);
}
