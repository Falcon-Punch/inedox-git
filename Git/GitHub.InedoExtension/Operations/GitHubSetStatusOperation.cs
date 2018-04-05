﻿using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Clients;
using Inedo.Extensions.Credentials;
using Inedo.Extensions.GitHub.SuggestionProviders;
using Inedo.Web;
using System.ComponentModel;
using System.Security;
using System.Threading.Tasks;

namespace Inedo.Extensions.GitHub.Operations
{
    [DisplayName("Set GitHub Build Status")]
    [Description("Sets a status message on a GitHub commit.")]
    [Example(@"try
{
    GitHub-Set-Status (
        Status = pending,
        ...
    );

    ...
}
catch
{
    # make sure the status is set even if the build fails.
    error;
}

GitHub-Set-Status (
    Status = auto,
    ...
);")]
    [ScriptAlias("GitHub-Set-Status")]
    [ScriptNamespace("GitHub", PreferUnqualified = true)]
    [Tag("source-control")]
    public sealed class GitHubSetStatusOperation : ExecuteOperation, IHasCredentials<GitHubCredentials>
    {
        public enum StatusType
        {
            pending,
            auto,
            success,
            failure,
            error
        }

        [DisplayName("Credentials")]
        [ScriptAlias("Credentials")]
        public string CredentialName { get; set; }

        [DisplayName("Additional context")]
        [Description("Appears in the commit status dialog on GitHub after \"ci/buildmaster\". Used to differentiate between multiple BuildMaster statuses on the same commit. In most cases, it is safe to leave this blank.")]
        [ScriptAlias("AdditionalContext")]
        public string AdditionalContext { get; set; }

        [Required]
        [DisplayName("Git commit hash")]
        [ScriptAlias("CommitHash")]
        public string CommitHash { get; set; }

        [Required]
        [DisplayName("Status")]
        [ScriptAlias("Status")]
        public StatusType Status { get; set; }

        [Category("Descriptions")]
        [DisplayName("Description")]
        [ScriptAlias("Description")]
        [Description("Used for all statuses except 'auto'")]
        [DefaultValue("#$ExecutionId in progress...")]
        public string Description { get; set; }

        [Category("Descriptions")]
        [DisplayName("Complete (success)")]
        [ScriptAlias("NormalDescription")]
        [DefaultValue("#$ExecutionId completed.")]
        public string NormalDescription { get; set; }

        [Category("Descriptions")]
        [DisplayName("Complete (warning)")]
        [ScriptAlias("WarningDescription")]
        [DefaultValue("#$ExecutionId completed with warnings.")]
        public string WarningDescription { get; set; }

        [Category("Descriptions")]
        [DisplayName("Complete (error)")]
        [ScriptAlias("ErrorDescription")]
        [DefaultValue("#$ExecutionId failed!")]
        public string ErrorDescription { get; set; }

        [Category("Connection/Identity")]
        [ScriptAlias("UserName")]
        [DisplayName("User name")]
        [PlaceholderText("Use user name from credentials")]
        [MappedCredential(nameof(GitCredentialsBase.UserName))]
        public string UserName { get; set; }

        [Category("Connection/Identity")]
        [ScriptAlias("Password")]
        [DisplayName("Password")]
        [PlaceholderText("Use password from credentials")]
        [MappedCredential(nameof(GitCredentialsBase.Password))]
        public SecureString Password { get; set; }

        [Category("GitHub")]
        [ScriptAlias("Organization")]
        [DisplayName("Organization name")]
        [MappedCredential(nameof(GitHubCredentials.OrganizationName))]
        [PlaceholderText("Use organization from credentials")]
        [SuggestableValue(typeof(OrganizationNameSuggestionProvider))]
        public string OrganizationName { get; set; }

        [Category("GitHub")]
        [ScriptAlias("Repository")]
        [DisplayName("Repository name")]
        [MappedCredential(nameof(GitHubCredentials.RepositoryName))]
        [PlaceholderText("Use repository from credentials")]
        [SuggestableValue(typeof(RepositoryNameSuggestionProvider))]
        public string RepositoryName { get; set; }

        [Category("Advanced")]
        [ScriptAlias("ApiUrl")]
        [DisplayName("API URL")]
        [PlaceholderText(GitHubClient.GitHubComUrl)]
        [Description("Leave this value blank to connect to github.com. For local installations of GitHub enterprise, an API URL must be specified.")]
        [MappedCredential(nameof(GitHubCredentials.ApiUrl))]
        public string ApiUrl { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var client = new GitHubClient(this.ApiUrl, this.UserName, this.Password, this.OrganizationName);

            var url = $"{SDK.BaseUrl.TrimEnd('/')}/executions/execution-in-progress?executionId={context.ExecutionId}";
            var statusContext = "ci/" + SDK.ProductName.ToLower() + AH.ConcatNE("/", this.AdditionalContext);

            if (this.Status == StatusType.auto)
            {
                switch (context.ExecutionStatus)
                {
                    case ExecutionStatus.Normal:
                        this.Status = StatusType.success;
                        this.Description = this.NormalDescription;
                        break;
                    case ExecutionStatus.Warning:
                        this.Status = StatusType.success;
                        this.Description = this.WarningDescription;
                        break;
                    case ExecutionStatus.Error:
                        this.Status = StatusType.failure;
                        this.Description = this.ErrorDescription;
                        break;
                    case ExecutionStatus.Fault:
                    default:
                        this.Status = StatusType.error;
                        this.Description = this.ErrorDescription;
                        break;
                }
            }

            this.LogInformation($"Assigning '{this.Status}' status to the commit on GitHub...");
            await client.CreateStatusAsync(AH.CoalesceString(this.OrganizationName, this.UserName), this.RepositoryName, this.CommitHash, this.Status.ToString(), url, this.Description, statusContext, context.CancellationToken).ConfigureAwait(false);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var credentials = string.IsNullOrEmpty(config[nameof(CredentialName)]) ? null : ResourceCredentials.Create<GitHubCredentials>(config[nameof(CredentialName)]);
            var repositoryOwner = AH.CoalesceString(config[nameof(OrganizationName)], credentials?.OrganizationName, config[nameof(UserName)], credentials?.UserName, "(unknown)");
            var repositoryName = AH.CoalesceString(config[nameof(RepositoryName)], credentials?.RepositoryName, "(unknown)");
            return new ExtendedRichDescription(
                new RichDescription("Set build status on GitHub commit ", new Hilite(config[nameof(CommitHash)]), " to ", new Hilite(config[nameof(Status)])),
                new RichDescription("in repository ", new Hilite(repositoryOwner), "/", new Hilite(repositoryName))
            );
        }
    }
}