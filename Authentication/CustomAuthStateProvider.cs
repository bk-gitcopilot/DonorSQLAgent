namespace DonorSQLAgent.Authentication
{
    using Microsoft.AspNetCore.Components.Authorization;
    using Microsoft.JSInterop;
    using System.Security.Claims;

    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IJSRuntime _jsRuntime;
        private ClaimsPrincipal anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        public CustomAuthStateProvider(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task MarkUserAsAuthenticated(string userName)
        {
            var expiry = DateTime.UtcNow.AddHours(1);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "userName", userName);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "expiry", expiry.ToString("o"));

            var identity = new ClaimsIdentity(new[]
            {
            new Claim(ClaimTypes.Name, userName)
        }, "apiauth");

            var user = new ClaimsPrincipal(identity);
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
        }

        public async Task MarkUserAsLoggedOut()
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "userName");
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "expiry");
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(anonymous)));
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var userName = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "userName");
            var expiryString = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "expiry");

            if (!string.IsNullOrEmpty(userName) && DateTime.TryParse(expiryString, out var expiry))
            {
                if (DateTime.UtcNow < expiry)
                {
                    var identity = new ClaimsIdentity(new[]
                    {
                    new Claim(ClaimTypes.Name, userName)
                }, "apiauth");

                    var user = new ClaimsPrincipal(identity);
                    return new AuthenticationState(user);
                }
                else
                {
                    // Expired – clear localStorage
                    await MarkUserAsLoggedOut();
                }
            }
            return new AuthenticationState(anonymous);
        }
    }



}
