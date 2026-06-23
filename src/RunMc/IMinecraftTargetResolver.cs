namespace RunMc;

public interface IMinecraftTargetResolver
{
    Task<MinecraftWindow> ResolveAsync(TargetKind target, CancellationToken cancellationToken);
}