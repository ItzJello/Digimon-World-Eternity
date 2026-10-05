namespace DigimonWorldEternity;

/// <summary>
/// Spawns a partner Digimon body with its eyes bound. Digimon GLBs are
/// human-sized, so bodies are scaled down to stand beside a tamer.
/// </summary>
public static class PartnerAvatar
{
    public const float AgumonScale = 0.4f;
    public const float PartnerScale = 0.8f;

    public static float ScaleFor(string slug) => slug == "agumon" ? AgumonScale : PartnerScale;

    public static ActorVisual SpawnCurrent()
    {
        PartnerRecord record = PartnerRoster.Find(GameSession.Current.PartnerSlug)
            ?? PartnerRoster.All[0];
        return Spawn(record.Slug, record.DisplayName);
    }

    public static ActorVisual Spawn(string slug, string name)
    {
        ActorVisual visual = ActorVisual.Spawn(RepoPaths.DigimonGlb(slug), name);
        EyeBinder.Apply(visual, RepoPaths.Eye(slug + ".png"), clipAlpha: true);
        return visual;
    }
}
