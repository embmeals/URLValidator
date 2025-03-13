using URLScan.Services;
using Microsoft.AspNetCore.Http.Json;
using URLScan.Models;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;
});

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

builder.Services.AddResponseCompression();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.MapControllers();

app.Run();