using System.Reflection;

using InterviewCoach.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMongoMemory(builder.Configuration);

builder.Services.AddMcpServer()
                .WithHttpTransport(o => o.Stateless = true)
                .WithToolsFromAssembly(Assembly.GetEntryAssembly());

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment() == false)
{
    app.UseHttpsRedirection();
}

app.MapMcp("/mcp");

await app.RunAsync();
