﻿// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Security.Claims;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using reactiveui.net.Formatters;
using reactiveui.net.Services;
using System.Net;

namespace reactiveui.net
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.cs.json")
                .AddJsonFile($"appsettings.cs.{env.EnvironmentName}.json", optional: true);

            if (env.IsDevelopment())
            {
                builder.AddUserSecrets<Startup>();
            }

            builder.AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));

            services.AddAuthentication(options => options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme);
            services.AddAuthorization(options =>
                options.AddPolicy("Admin", policyBuilder =>
                    policyBuilder.RequireClaim(
                        ClaimTypes.Name,
                        Configuration["Authorization:AdminUsers"].Split(',')
                    )
                )
            );
            services.AddMvc(options => options.OutputFormatters.Add(new iCalendarOutputFormatter()))
                .AddCookieTempDataProvider();

            services.AddCachedWebRoot();
            services.AddSingleton<IStartupFilter, AppStart>();
            services.AddScoped<IShowsService, YouTubeShowsService>();
            services.AddSingleton<IDeploymentEnvironment, DeploymentEnvironment>();
            services.AddSingleton<ITelemetryInitializer, EnvironmentTelemetryInitializer>();
            services.AddSingleton<IConfigureOptions<ApplicationInsightsServiceOptions>, ApplicationInsightsServiceOptionsSetup>();

            if (string.IsNullOrEmpty(Configuration["AppSettings:AzureStorageConnectionString"]))
            {
                services.AddSingleton<ILiveShowDetailsService, FileSystemLiveShowDetailsService>();
            }
            else
            {
                services.AddSingleton<ILiveShowDetailsService, AzureStorageLiveShowDetailsService>();
            }
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                // If we're behind IIS in development don't bother logging to the console as the same data is easily
                // available in the Visual Studio Application Insights search window
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_TOKEN")))
                {
                    loggerFactory.AddConsole(Configuration.GetSection("Logging"));
                }
                loggerFactory.AddDebug();
                app.UseDeveloperExceptionPage();
            }
            else
            {
                loggerFactory.AddApplicationInsights(app.ApplicationServices, LogLevel.Error);
                app.UseExceptionHandler("/error");
            }

            app.UseHsts();

            app.UseRewriter(new RewriteOptions()
#if !DEBUG
                .AddRedirectToHttpsPermanent()
#endif
            );

            app.UseStatusCodePages();

            app.UseDefaultFiles();  // serve requests to /dashboard/ so user doesn't have to append "index.html"
            app.UseStaticFiles();

            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AutomaticAuthenticate = true
            });

            app.UseOpenIdConnectAuthentication(new OpenIdConnectOptions
            {
                AutomaticAuthenticate = true,
                AutomaticChallenge = true,
                ClientId = Configuration["Authentication:AzureAd:ApplicationId"],
                Authority = Configuration["Authentication:AzureAd:AADInstance"] + Configuration["Authentication:AzureAd:TenantId"],
                ResponseType = OpenIdConnectResponseType.IdToken,
                SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme
            });

            app.Use((context, next) => context.Request.Path.StartsWithSegments("/ping")
                ? context.Response.WriteAsync("pong")
                : next()
            );

            app.UseMvc();
        }
    }
}
