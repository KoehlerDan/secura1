using System.Net.Http;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Nuke.Common.CI.AzurePipelines;

[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
[AzurePipelines(
    AzurePipelinesImage.UbuntuLatest,
    AzurePipelinesImage.WindowsLatest,
    AzurePipelinesImage.MacOsLatest
 )]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Deploy_Nomad);

    [Parameter("Boolean value for whether or not developer is providing a buildfile")]
    readonly bool dockerFile = false;

    [Parameter("Specifies what to name the image")]
    readonly string imageName;

    [Parameter("Specifies what to tag the image version")]
    readonly string tagValue;

    [Solution] readonly Solution Solution;

    AbsolutePath SourceDirectory => RootDirectory / "source";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";

    private static readonly HttpClient client = new HttpClient();

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration.Release)
                .EnableNoRestore());
        });

    [PathExecutable] readonly Tool AZ;
    Target ACR_Login => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            AZ("acr login --name securanonprod");
        });

    [PathExecutable] private readonly Tool Docker;
    [PathExecutable] private readonly Tool PACK;
    Target Docker_Build => _ => _
        .DependsOn(ACR_Login)
        .Executes(() =>
        {
            if (dockerFile)
            {
                Docker("build . --tag " + imageName);
            }
            else
            {
                PACK("build " + imageName + " --buildpack gcr.io/paketo-buildpacks/dotnet-core   --builder paketobuildpacks/builder:base --path ./DkWebApp");
            }
        });

    Target Docker_Tag => _ => _
        .DependsOn(Docker_Build)
        .Executes(() =>
        {
            Docker("tag " + imageName + " securanonprod.azurecr.io/" + imageName + ":" + tagValue);
        });
    Target Docker_Push => _ => _
        .DependsOn(Docker_Tag)
        .Executes(() =>
        {
            Docker("push securanonprod.azurecr.io/" + imageName + ":" + tagValue);
        });
    Target Post_Template => _ => _
        .DependsOn(Docker_Push)
        .Executes(() =>
        {
            JObject o1 = JObject.Parse(File.ReadAllText("./DKWebApp/simpleTemplate.json"));
            string returnValue;
            using (var content = new StringContent(JsonConvert.SerializeObject(o1), System.Text.Encoding.UTF8, "application/json"))
            {
                HttpResponseMessage result = client.PostAsync("http://172.23.0.31:9999/TemplateAPIService/api/template/simple/create", content).Result;
                returnValue = result.Content.ReadAsStringAsync().Result;
            }
            Logger.Normal(returnValue);
            File.WriteAllText(imageName + ".nomad", returnValue);
        });
    [PathExecutable] readonly Tool NOMAD;
    Target Deploy_Nomad => _ => _
        .DependsOn(Post_Template)
        .Executes(() =>
        {
            NOMAD("job run -address=\"http://ho-nomad-p01:4646\" -token=09a88da8-50ab-d768-a7bb-ce04885998f6 " + imageName + ".nomad");
        });
}