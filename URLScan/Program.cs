using URLScan.Services;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient<IUrlValidator, UrlValidator>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
    });

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.WriteIndented = false;
    options.SerializerOptions.MaxDepth = 64;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();