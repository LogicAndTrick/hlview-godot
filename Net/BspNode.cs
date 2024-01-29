using System.Collections.Generic;
using System.IO;
using Sledge.Formats.Bsp;
using Sledge.Formats.Bsp.Objects;
using FileAccess = System.IO.FileAccess;

namespace HLView.Net;

public partial class BspNode : Node3D
{
    private MeshInstance3D _meshInstance;
    private Mesh _mesh;

	public override void _Ready()
	{
        _meshInstance = new MeshInstance3D();
        AddChild(_meshInstance);
	}

	public override void _Process(double delta)
	{
		//
	}

    [Signal]
    public delegate void LoadCompletedEventHandler();

    public void Clear()
    {
        _meshInstance.Mesh = null;
        _mesh?.Dispose();

        foreach (var v in _materials) v.Dispose();
        _materials.Clear();

        foreach (var (_, v) in _textures) v.Dispose();
        _textures.Clear();

        foreach (var (_, v) in _images) v.Dispose();
        _images.Clear();
    }

    public void LoadFile(string fileName)
    {
        GD.Print(fileName);
        Clear();

        using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bsp = new BspFile(stream);
        var mesh = new ArrayMesh();
        var model = bsp.Models[0];
        GenerateTexturesForBsp(bsp);
        GenerateGeometryForModel(mesh, model, bsp);
        _meshInstance.Mesh = mesh;

        EmitSignal(SignalName.LoadCompleted);
    }

    private readonly System.Collections.Generic.List<Material> _materials = new();
    private readonly System.Collections.Generic.Dictionary<int, Texture2D> _textures = new();
    private readonly System.Collections.Generic.Dictionary<int, Image> _images = new();

    private void GenerateTexturesForBsp(BspFile bsp)
    {
        for (var i = 0; i < bsp.Textures.Count; i++)
        {
            var tex = bsp.Textures[i];

            var mip = tex.MipData[0];
            var convertedData = new byte[mip.Length * 4];
            System.Array.Fill(convertedData, byte.MaxValue);
            for (var j = 0; j < mip.Length; j++)
            {
                System.Array.Copy(tex.Palette, mip[j] * 3, convertedData, j * 4, 3);
            }

            var image = Image.CreateFromData((int) tex.Width, (int)tex.Height, false, Image.Format.Rgba8, convertedData);
            _images.Add(i, image);

            var texture = ImageTexture.CreateFromImage(image);
            _textures.Add(i, texture);
        }
    }

    private void GenerateGeometryForModel(ArrayMesh mesh, Model model, BspFile bsp)
    {
        var faces = new List<Face>();
        for (var i = model.FirstFace; i < model.FirstFace + model.NumFaces; i++)
        {
            var face = bsp.Faces[i];
            faces.Add(face);
        }
        faces.Sort((a, b) =>
        {
            var ta = bsp.Texinfo[a.TextureInfo];
            var tb = bsp.Texinfo[b.TextureInfo];
            return ta.MipTexture.CompareTo(tb.MipTexture);
        });

        foreach (var face in faces.Take(200))
        {
            var plane = bsp.Planes[face.Plane];
            var texInfo = bsp.Texinfo[face.TextureInfo];
            var mipTexture = bsp.Textures[texInfo.MipTexture];

            var vertices = new List<LightmappedVertex>();

            // map the vertices
            for (var j = face.FirstEdge; j < face.FirstEdge + face.NumEdges; j++)
            {
                var edgeIndex = bsp.Surfedges[j];
                var edge = bsp.Edges[Math.Abs(edgeIndex)];

                var vertexIndex = edgeIndex > 0 ? edge.Start : edge.End;
                var vertex = bsp.Vertices[vertexIndex].ToGodotVector3();

                var normal = plane.Normal.ToGodotVector3();

                var sn = texInfo.S.ToGodotVector3();
                var u = vertex.Dot(sn) + texInfo.S.W;

                var tn = texInfo.T.ToGodotVector3();
                var v = vertex.Dot(tn) + texInfo.T.W;

                // todo lightmaps
                vertices.Add(new LightmappedVertex
                {
                    Position = vertex,
                    Normal = normal,
                    Texture = new Vector2(u, v),
                    Color = Colors.White,
                    Lightmap = default
                });
            }

            var mipTextureSize = new Vector2(mipTexture.Width, mipTexture.Height);

            var surfaceTool = new SurfaceTool();
            surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

            // create the material
            var material = new StandardMaterial3D();
            material.AlbedoTexture = _textures[texInfo.MipTexture];
            material.AOOnUV2 = true;
            material.AOEnabled = true;
            // material.AOTexture
            material.TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmapsAnisotropic;
            _materials.Add(material);
            surfaceTool.SetMaterial(material);

            // triangulate the face
            for (var j = 1; j < face.NumEdges - 1; j++)
            {
                var a = vertices[0];
                var c = vertices[j + 1];
                var b = vertices[j];

                surfaceTool.SetUV(a.Texture / mipTextureSize);
                surfaceTool.SetNormal(a.Normal);
                surfaceTool.AddVertex(a.Position);

                surfaceTool.SetUV(b.Texture / mipTextureSize);
                surfaceTool.SetNormal(b.Normal);
                surfaceTool.AddVertex(b.Position);

                surfaceTool.SetUV(c.Texture / mipTextureSize);
                surfaceTool.SetNormal(c.Normal);
                surfaceTool.AddVertex(c.Position);
            }

            surfaceTool.Commit(mesh);
        }
    }
    private class LightmappedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 Texture;
        public Color Color;
        public Vector2 Lightmap;
    }
}
