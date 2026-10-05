using Godot;
using System.Collections.Generic;

namespace DigimonWorldEternity;

public static class MeshQuery
{
    public static List<MeshInstance3D> Find(Node node)
    {
        var found = new List<MeshInstance3D>();
        Collect(node, found);
        return found;
    }

    private static void Collect(Node node, List<MeshInstance3D> found)
    {
        if (node is MeshInstance3D mesh)
            found.Add(mesh);
        foreach (Node child in node.GetChildren())
            Collect(child, found);
    }
}
