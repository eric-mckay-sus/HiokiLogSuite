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

// Isolate PowerSearchLogic and its dependencies so it does not interact with the other pages
builder.Services.AddScoped<PowerSearchLogic>(sp =>
{
    var dbFactory = sp.GetRequiredService<IDbContextFactory<LogDbContext>>();
    var js = sp.GetRequiredService<IJSService>();
    var nav = sp.GetRequiredService<INavService>();
    var parser = sp.GetRequiredService<SearchParserService>();

    var privateTables = new List<ILogTableLogic>
    {
        new GroupTableLogic(dbFactory, js, nav),
        new StepTableLogic(dbFactory, js, nav),
        new FctTableLogic(dbFactory, js, nav)
    };

    return new PowerSearchLogic(privateTables, parser, nav, js);
});

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
