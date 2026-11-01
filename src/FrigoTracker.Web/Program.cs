using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FrigoTracker.Web;
using FrigoTracker.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Supabase settings come from wwwroot/appsettings.json (public URL + public key only).
var supabase = new SupabaseOptions
{
    // Accept the "Data API" URL too (…/rest/v1/): only the project base URL is needed.
    Url = (builder.Configuration["Supabase:Url"] ?? "").Trim().TrimEnd('/').Replace("/rest/v1", ""),
    AnonKey = builder.Configuration["Supabase:AnonKey"] ?? "",
};
builder.Services.AddSingleton(supabase);
builder.Services.AddSingleton<SupabaseClient>();

// One user per browser tab: one shared state instance is enough.
builder.Services.AddSingleton<AppState>();

await builder.Build().RunAsync();
