using Godot;

namespace DigimonWorldEternity;

/// <summary>
/// A skinned GLB. These meshes face +Z, so a yaw of 0 looks down +Z.
/// </summary>
public partial class ActorVisual : Node3D
{
    private AnimationPlayer? _anim;
    private string? _idleClip;
    private string? _walkClip;
    private string? _runClip;
    private string? _sitIntro;
    private string? _sitLoop;
    private bool _walking;
    private bool _running;
    private bool _sitting;
    private string? _actionClip;
    private bool _downed;
    private bool _poseHeld;
    private bool _victorious;
    private bool _looping;

    public bool IsWalking => _walking;

    public static ActorVisual Spawn(string glbPath, string name)
    {
        ActorVisual visual = Adopt(GlbLoader.Load(glbPath, "Model"), name);
        EyeBinder.ApplyPackedIris(visual, glbPath);
        return visual;
    }

    public static ActorVisual Adopt(Node model, string name)
    {
        var visual = new ActorVisual { Name = name };
        visual.AddChild(model);
        DullCoat(visual);
        FaceEyes.Apply(visual);
        visual._anim = FindAnim(visual);
        // fn01 is the field idle. bn01 is the battle idle. bn02 is the tired one.
        // br01 is the run. bs01 is a one-shot pose, not a stand.
        visual._idleClip = FindClip(visual._anim, "fn01_01", "fn01", "bn01");
        visual._walkClip = FindClip(visual._anim, "fw01_01", "fw01", "br01");
        visual._runClip = FindClip(visual._anim, "fr01_01", "fr01");
        visual._sitLoop = FindClip(visual._anim, "fn02_02");
        visual._sitIntro = FindClip(visual._anim, "fn02_01", "fn02");
        ForceLoop(visual._anim, visual._idleClip);
        ForceLoop(visual._anim, visual._walkClip);
        ForceLoop(visual._anim, visual._runClip);
        ForceLoop(visual._anim, visual._sitLoop);
        if (visual._anim != null)
            visual._anim.AnimationFinished += visual.OnAnimationFinished;
        visual.PlayIdle();
        return visual;
    }

    public void PlayIdle()
    {
        if (_downed || _victorious || _looping)
            return;
        _walking = false;
        Play(_idleClip);
    }

    public void SetWalking(bool walking)
    {
        SetPace(walking, false);
    }

    /// <summary>
    /// Walk uses the field walk. Shift uses the field run when the model has one.
    /// </summary>
    public void SetPace(bool moving, bool running)
    {
        if (_downed || _victorious || _looping)
            return;
        running = moving && running && _runClip != null;
        if (moving == _walking && running == _running && !_sitting)
            return;
        _walking = moving;
        _running = running;
        if (!moving)
        {
            if (!_sitting)
                Play(_idleClip);
            return;
        }
        _sitting = false;
        Play(running ? _runClip : _walkClip);
    }

    /// <summary>
    /// Plays a technique clip once, then returns to idle.
    /// The needle is a suffix such as e002 or ba01.
    /// </summary>
    public void PlayAction(string needle, bool fallback = true)
    {
        if (_downed || _victorious)
            return;
        string? clip = FindClipEnding(_anim, needle);
        if (clip == null && fallback)
            clip = FindClip(_anim, "bs01", "ba01");
        if (clip == null)
            return;
        _walking = false;
        _running = false;
        _sitting = false;
        _actionClip = clip;
        SetLoop(_anim, clip, Animation.LoopModeEnum.None);
        Play(clip);
    }

    /// <summary>Every clip name on this model, as stored in the file.</summary>
    public string[] ClipNames()
    {
        return _anim == null ? System.Array.Empty<string>() : _anim.GetAnimationList();
    }

    /// <summary>Plays one clip and keeps repeating it.</summary>
    public void PlayClipLoop(string clip)
    {
        if (_anim == null || string.IsNullOrEmpty(clip) || !_anim.HasAnimation(clip))
            return;
        _looping = true;
        _downed = false;
        _victorious = false;
        _walking = false;
        _running = false;
        _sitting = false;
        _actionClip = clip;
        SetLoop(_anim, clip, Animation.LoopModeEnum.Linear);
        Play(clip);
    }

    /// <summary>
    /// Win pose (bv01). Loops until the bout leaves.
    /// </summary>
    public void PlayVictory()
    {
        if (_downed || _anim == null)
            return;
        string? clip = FindClipEnding(_anim, "bv01");
        if (clip == null)
            return;
        _victorious = true;
        _walking = false;
        _running = false;
        _sitting = false;
        _actionClip = clip;
        SetLoop(_anim, clip, Animation.LoopModeEnum.Linear);
        Play(clip);
    }

    /// <summary>
    /// Knockdown (bd02). Plays once, then locks the last pose so the
    /// skeleton does not fall back to the bind pose.
    /// </summary>
    public void PlayDown()
    {
        if (_downed || _anim == null)
            return;
        string? clip = FindClipEnding(_anim, "bd02");
        if (clip == null)
            return;
        _downed = true;
        _walking = false;
        _running = false;
        _sitting = false;
        _actionClip = clip;
        SetLoop(_anim, clip, Animation.LoopModeEnum.None);
        Play(clip);
        Animation? anim = _anim.GetAnimation(clip);
        if (anim == null)
            return;
        SceneTree? tree = GetTree();
        if (tree == null)
            return;
        tree.CreateTimer(anim.Length).Timeout += HoldDownPose;
    }

    public void PlayReaction(string needle)
    {
        if (_downed || _victorious)
            return;
        string? clip = FindClipEnding(_anim, needle);
        if (clip == null)
            return;
        _walking = false;
        _running = false;
        _sitting = false;
        _actionClip = clip;
        SetLoop(_anim, clip, Animation.LoopModeEnum.None);
        Play(clip);
    }

    /// <summary>
    /// Plays the sit-down once, then holds the sitting loop.
    /// Standing idle stays up until the caller decides to sit.
    /// </summary>
    public void BeginSit()
    {
        if (_downed || _walking || _sitting || (_sitIntro == null && _sitLoop == null))
            return;
        _sitting = true;
        if (_sitIntro != null && _sitIntro != _sitLoop)
        {
            SetLoop(_anim, _sitIntro, Animation.LoopModeEnum.None);
            Play(_sitIntro);
            return;
        }
        Play(_sitLoop);
    }

    private void OnAnimationFinished(StringName name)
    {
        if (_looping || _victorious)
            return;
        if (_downed)
        {
            if (_actionClip != null && name == _actionClip)
                CallDeferred(nameof(HoldDownPose));
            return;
        }
        if (_actionClip != null && name == _actionClip)
        {
            _actionClip = null;
            PlayIdle();
            return;
        }
        if (!_sitting || _sitIntro == null || name != _sitIntro)
            return;
        if (_sitLoop != null)
            Play(_sitLoop);
    }

    /// <summary>
    /// Sample the last key of the knockdown and keep that pose on the skeleton.
    /// Seeking the exact clip length falls off the last key onto the bind pose.
    /// </summary>
    private void HoldDownPose()
    {
        if (_poseHeld || !_downed || _anim == null || string.IsNullOrEmpty(_actionClip) || !_anim.HasAnimation(_actionClip))
            return;
        _poseHeld = true;
        Animation? anim = _anim.GetAnimation(_actionClip);
        if (anim == null)
            return;

        double last = 0;
        for (int track = 0; track < anim.GetTrackCount(); track++)
        {
            int keys = anim.TrackGetKeyCount(track);
            if (keys <= 0)
                continue;
            last = System.Math.Max(last, anim.TrackGetKeyTime(track, keys - 1));
        }

        _anim.Seek(last, true);
        var poses = new System.Collections.Generic.List<(Skeleton3D Skeleton, int Bone, Vector3 Position, Quaternion Rotation, Vector3 Scale)>();
        foreach (Node node in FindChildren("*", "Skeleton3D", true, false))
        {
            if (node is not Skeleton3D skeleton)
                continue;
            int count = skeleton.GetBoneCount();
            for (int bone = 0; bone < count; bone++)
            {
                poses.Add((
                    skeleton,
                    bone,
                    skeleton.GetBonePosePosition(bone),
                    skeleton.GetBonePoseRotation(bone),
                    skeleton.GetBonePoseScale(bone)));
            }
        }

        _anim.Pause();
        _anim.Active = false;
        foreach ((Skeleton3D skeleton, int bone, Vector3 position, Quaternion rotation, Vector3 scale) in poses)
        {
            skeleton.SetBonePosePosition(bone, position);
            skeleton.SetBonePoseRotation(bone, rotation);
            skeleton.SetBonePoseScale(bone, scale);
        }
    }

    private void Play(string? clip)
    {
        if (_anim == null || string.IsNullOrEmpty(clip) || !_anim.HasAnimation(clip))
            return;
        _anim.Play(clip);
        _anim.Advance(0);
    }

    /// <summary>
    /// The body mask was imported as a metal map, which turns the coat into a highlight.
    /// Keep the color and the normal, and shade it as cloth.
    /// </summary>
    private static void DullCoat(Node3D actor)
    {
        foreach (MeshInstance3D mesh in MeshQuery.Find(actor))
        {
            if (mesh.Mesh == null)
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D source)
                    continue;
                var copy = (StandardMaterial3D)source.Duplicate();
                copy.Metallic = 0;
                copy.MetallicTexture = null;
                copy.Roughness = 0.82f;
                copy.RoughnessTexture = null;
                copy.MetallicSpecular = 0.12f;
                string meshName = mesh.Name.ToString().ToLowerInvariant();
                if (meshName.Contains("outline"))
                {
                    mesh.Visible = false;
                    mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                    continue;
                }
                mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                // Show the imported color sheet. A grey multiply was flattening
                // the outfit and the face into one flat tone.
                copy.AlbedoColor = Colors.White;
                copy.VertexColorUseAsAlbedo = false;
                if (meshName == "face")
                    copy.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
                else if (!IsSkinOrFace(meshName))
                    copy.NormalTexture = null;
                mesh.SetSurfaceOverrideMaterial(surface, copy);
            }
        }
    }

    private static bool IsSkinOrFace(string meshName)
    {
        return meshName.Contains("skin")
            || meshName is "face" or "eye" or "sirome" or "eye_h" or "eye_s" or "eye_blow" or "eyelash"
            || meshName.Contains("hair")
            || meshName.Contains("lash")
            || meshName.Contains("brow");
    }

    private static string? FindClipEnding(AnimationPlayer? player, string needle)
    {
        if (player == null || string.IsNullOrEmpty(needle))
            return null;
        string suffix = "_" + needle;
        foreach (string clip in player.GetAnimationList())
        {
            if (clip == needle || clip.EndsWith(suffix))
                return clip;
        }
        return null;
    }

    private static string? FindClip(AnimationPlayer? player, params string[] needles)
    {
        if (player == null)
            return null;
        string[] clips = player.GetAnimationList();
        foreach (string needle in needles)
        {
            foreach (string clip in clips)
            {
                if (clip.Contains(needle))
                    return clip;
            }
        }
        return null;
    }

    private static void ForceLoop(AnimationPlayer? player, string? clip)
    {
        SetLoop(player, clip, Animation.LoopModeEnum.Linear);
    }

    private static void SetLoop(AnimationPlayer? player, string? clip, Animation.LoopModeEnum mode)
    {
        if (player == null || string.IsNullOrEmpty(clip) || !player.HasAnimation(clip))
            return;
        Animation? anim = player.GetAnimation(clip);
        if (anim != null)
            anim.LoopMode = mode;
    }

    private static AnimationPlayer? FindAnim(Node node)
    {
        if (node is AnimationPlayer player)
            return player;
        foreach (Node child in node.GetChildren())
        {
            AnimationPlayer? found = FindAnim(child);
            if (found != null)
                return found;
        }
        return null;
    }
}
