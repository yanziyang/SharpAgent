using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Workspaces;

namespace SharpAgent.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("Workspaces");

        builder.HasKey(static workspace => workspace.Id);
        builder.Property(static workspace => workspace.Id).HasMaxLength(64);
        builder.Property(static workspace => workspace.Name).HasMaxLength(200);
        builder.Property(static workspace => workspace.RootPath).HasMaxLength(1_024);
        builder.Property(static workspace => workspace.CanonicalRootPath).HasMaxLength(1_024);
        builder.Property(static workspace => workspace.AllowedPathsJson).HasMaxLength(8_000);
        builder.Property(static workspace => workspace.DefaultModelProfileId).HasMaxLength(64);
        builder.Property(static workspace => workspace.ValidationMessage).HasMaxLength(500);

        builder.Property(static workspace => workspace.Status).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(static workspace => workspace.Name);
    }
}

internal sealed class ModelProfileConfiguration : IEntityTypeConfiguration<ModelProfile>
{
    public void Configure(EntityTypeBuilder<ModelProfile> builder)
    {
        builder.ToTable("ModelProfiles");

        builder.HasKey(static profile => profile.Id);
        builder.Property(static profile => profile.Id).HasMaxLength(64);
        builder.Property(static profile => profile.DisplayName).HasMaxLength(200);
        builder.Property(static profile => profile.ProviderModelId).HasMaxLength(200);
        builder.Property(static profile => profile.CapabilitiesJson).HasMaxLength(4_000);
        builder.Property(static profile => profile.ConfigReference).HasMaxLength(300);
        builder.Property(static profile => profile.ValidationMessage).HasMaxLength(500);

        builder.Property(static profile => profile.Provider).HasConversion<string>().HasMaxLength(32);
        builder.Property(static profile => profile.EndpointKind).HasConversion<string>().HasMaxLength(32);
        builder.Property(static profile => profile.ValidationStatus).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(static profile => profile.DisplayName);
    }
}

internal sealed class PolicyProfileConfiguration : IEntityTypeConfiguration<PolicyProfile>
{
    public void Configure(EntityTypeBuilder<PolicyProfile> builder)
    {
        builder.ToTable("PolicyProfiles");

        builder.HasKey(static policy => policy.Id);
        builder.Property(static policy => policy.Id).HasMaxLength(64);
        builder.Property(static policy => policy.Name).HasMaxLength(200);
        builder.Property(static policy => policy.RulesJson).HasMaxLength(16_000);
    }
}
