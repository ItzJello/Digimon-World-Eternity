namespace DigimonWorldEternity;

public static class PlayerAvatar
{
    public static ActorVisual SpawnCurrent()
    {
        var session = GameSession.Current;
        ActorVisual visual = ActorVisual.Spawn(
            CharacterRoster.PathFor(session.PlayerModel, session.PlayerOutfit),
            "Player");
        FaceEyes.Apply(visual);
        return visual;
    }
}
