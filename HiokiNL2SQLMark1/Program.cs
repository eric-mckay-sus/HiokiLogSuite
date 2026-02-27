using HiokiNL2SQLMark1.Components;
using Microsoft.EntityFrameworkCore;
using HiokiNL2SQLMark1;
using HiokiNL2SQLMark1.Logic;
using HiokiNL2SQLMark1.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection"); // from appsettings.json, no idea how this looks in production

builder.Services.AddDbContextFactory<LogDbContext>(options =>
    options.UseSqlServer(connectionString));

// Logic for the visual query builders.
builder.Services.AddScoped<GroupTableLogic>();
builder.Services.AddScoped<StepTableLogic>();
builder.Services.AddScoped<FctTableLogic>();

// Logic for the power search page
builder.Services.AddScoped<PowerSearchLogic>();
builder.Services.AddTransient<ILogTableLogic>(sp => sp.GetRequiredService<GroupTableLogic>());
builder.Services.AddTransient<ILogTableLogic>(sp => sp.GetRequiredService<StepTableLogic>());
builder.Services.AddTransient<ILogTableLogic>(sp => sp.GetRequiredService<FctTableLogic>());

// Services for the entire app
builder.Services.AddScoped<INavService, NavService>();
builder.Services.AddScoped<IJSService, JSService>();
builder.Services.AddScoped<SearchParserService>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddBlazorBootstrap();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
