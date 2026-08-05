// <copyright file="Program.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace HiokiNL2SQL;

using Microsoft.EntityFrameworkCore;

using HiokiNL2SQL.Components;
using HiokiNL2SQL.Logic;
using HiokiNL2SQL.Services;
using InterProcessIO;

/// <summary>
/// Hosts the application startup and configuration.
/// </summary>
public static class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Command-line arguments supplied by the host.</param>
    public static void Main(string[] args)
    {
        // Pre-check environment variables
        try
        {
            Config.GetConnectionString();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDbContextFactory<LogDbContext>(options =>
            options.UseSqlServer(Config.GetConnectionString()));

        builder.Services.AddTransient<BlazorInputProvider>();
        builder.Services.AddTransient<IInputProvider>(sp => sp.GetRequiredService<BlazorInputProvider>());
        builder.Services.AddTransient<BlazorReporter>();
        builder.Services.AddTransient<IOutputProvider>(sp => sp.GetRequiredService<BlazorReporter>());

        // Allow large multi-file selection payloads from InputFile to pass over SignalR.
        builder.Services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 64 * 1024 * 1024;
        });

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
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddBlazorBootstrap();

        WebApplication app = builder.Build();

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
    }
}
