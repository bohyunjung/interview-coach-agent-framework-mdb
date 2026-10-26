#:sdk Aspire.AppHost.Sdk@13.6.4
#:property NoWarn=$(NoWarn);ASPIRE010
#:project ./src/InterviewCoach.Agent/InterviewCoach.Agent.csproj
#:project ./src/InterviewCoach.AppHost.Core/InterviewCoach.AppHost.Core.csproj
#:project ./src/InterviewCoach.Mcp.Memory/InterviewCoach.Mcp.Memory.csproj
#:project ./src/InterviewCoach.WebUI/InterviewCoach.WebUI.csproj

using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var config = builder.Configuration
                    .AddJsonFile("apphost.settings.json", optional: true, reloadOnChange: true)
                    .AddUserSecrets(typeof(Program).Assembly, optional: true, reloadOnChange: true)
                    .Build();

var mcpMarkItDown = builder.AddContainer(ResourceConstants.McpMarkItDown, "mcp/markitdown", "latest")
                           .WithHttpEndpoint(targetPort: 3001)
                           .WithArgs("--http", "--host", "0.0.0.0", "--port", "3001");

// MongoDB Atlas hosts the agent memory. The connection string and the Voyage AI key are
// user-provided parameters (see docs/PREREQUISITES.md), not local containers.
var mongodb = builder.AddConnectionString(ResourceConstants.MongoDb);
var voyageApiKey = builder.AddParameter(ResourceConstants.VoyageApiKey, secret: true);

var mcpMemory = builder.AddProject<Projects.InterviewCoach_Mcp_Memory>(ResourceConstants.McpMemory)
                       .WithReference(mongodb)
                       .WithEnvironment("Voyage__ApiKey", voyageApiKey);

var agent = builder.AddProject<Projects.InterviewCoach_Agent>(ResourceConstants.Agent)
                   .WithExternalHttpEndpoints()
                   .WithLlmReference(config, args)
                   .WithReference(mcpMarkItDown.GetEndpoint("http"))
                   .WithReference(mongodb)
                   .WithReference(mcpMemory)
                   .WaitFor(mcpMarkItDown)
                   .WaitFor(mcpMemory);

var webUI = builder.AddProject<Projects.InterviewCoach_WebUI>(ResourceConstants.WebUI)
                   .WithExternalHttpEndpoints()
                   .WithReference(agent)
                   .WaitFor(agent);

await builder.Build().RunAsync();
