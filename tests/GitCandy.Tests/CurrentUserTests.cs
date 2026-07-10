using System.Security.Claims;
using GitCandy.Authentication;
using GitCandy.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace GitCandy.Tests;

[TestClass]
public sealed class CurrentUserTests
{
    [TestMethod]
    public void CurrentUser_WithAuthenticatedAdministrator_ExposesIdentityClaims()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-admin"),
                    new Claim(ClaimTypes.Name, "admin"),
                    new Claim(ClaimTypes.Role, RoleNames.Administrator)
                ],
                IdentityConstants.ApplicationScheme,
                ClaimTypes.Name,
                ClaimTypes.Role))
        };
        httpContext.RequestAborted = cancellationTokenSource.Token;
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        Assert.IsTrue(currentUser.IsAuthenticated);
        Assert.AreEqual("user-admin", currentUser.UserId);
        Assert.AreEqual("admin", currentUser.UserName);
        Assert.IsTrue(currentUser.IsAdministrator);
        Assert.AreEqual(cancellationTokenSource.Token, currentUser.RequestAborted);
        Assert.AreSame(httpContext.User, currentUser.Principal);
    }

    [TestMethod]
    public void CurrentUser_WithoutHttpContext_ReturnsAnonymousDefaults()
    {
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor());

        Assert.IsFalse(currentUser.IsAuthenticated);
        Assert.IsNull(currentUser.UserId);
        Assert.IsNull(currentUser.UserName);
        Assert.IsFalse(currentUser.IsAdministrator);
        Assert.AreEqual(CancellationToken.None, currentUser.RequestAborted);
    }
}
