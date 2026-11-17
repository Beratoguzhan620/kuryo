using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kuryo.Api.Features.Demo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kuryo.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DemoTests(ApiFactory factory)
{
    [Fact]
    public async Task Outside_the_public_demo_there_are_no_demo_sign_ins()
    {
        using var anonymous = factory.CreateClient();

        var accounts = await anonymous.GetFromJsonAsync<List<JsonElement>>("/api/demo");

        Assert.NotNull(accounts);
        Assert.Empty(accounts);
    }

    [Fact]
    public async Task The_demo_company_sets_itself_up_and_its_couriers_deliver_orders()
    {
        var demo = new DemoOptions { Enabled = true, OrderEvery = TimeSpan.FromSeconds(30) };
        var worker = new DemoWorker(Options.Create(demo), new TestClients(factory), factory.Services.GetRequiredService<IHostApplicationLifetime>(),
            factory.Clock, NullLogger<DemoWorker>.Instance);
        using var http = factory.CreateClient();

        await worker.SetUpAsync(http, CancellationToken.None);
        for (var i = 0; i < 120 && !await DeliveredAsync(http, demo); i++)
        {
            await factory.MatchAsync();
            await worker.TickAsync(http, CancellationToken.None);
            factory.Clock.Advance(TimeSpan.FromSeconds(20));
        }

        Assert.True(await DeliveredAsync(http, demo));

        // Setting up again, as after a restart, finds the same company instead of making another.
        await worker.SetUpAsync(http, CancellationToken.None);
        using var dispatcher = await SignedInAsync(http, demo.DispatcherEmail, demo.Password);
        var me = await dispatcher.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1, me.GetProperty("companies").GetArrayLength());

        // The published merchant and courier can sign in and belong to the company.
        foreach (var email in new[] { demo.MerchantEmail, demo.CourierEmail })
        {
            using var member = await SignedInAsync(http, email, demo.Password);
            var theirs = await member.GetFromJsonAsync<JsonElement>("/api/me");
            Assert.Equal(1, theirs.GetProperty("companies").GetArrayLength());
        }
    }

    private async Task<bool> DeliveredAsync(HttpClient http, DemoOptions demo)
    {
        using var dispatcher = await SignedInAsync(http, demo.DispatcherEmail, demo.Password);
        var me = await dispatcher.GetFromJsonAsync<JsonElement>("/api/me");
        var company = me.GetProperty("companies")[0].GetProperty("id").GetString();
        var delivered = await dispatcher.GetFromJsonAsync<JsonElement>($"/api/companies/{company}/orders?status=Delivered");
        return delivered.GetProperty("total").GetInt32() > 0;
    }

    private async Task<HttpClient> SignedInAsync(HttpClient http, string email, string password)
    {
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed class TestClients(ApiFactory factory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => factory.CreateClient();
    }
}
