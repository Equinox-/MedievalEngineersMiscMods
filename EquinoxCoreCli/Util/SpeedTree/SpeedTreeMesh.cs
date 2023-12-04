using System.Xml.Schema;
using System.Xml.Serialization;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.SpeedTree
{
    public class SpeedTreeRawObjectsObjectPoints
    {
        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> X { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> Y { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> Z { get; set; }

        public Vector3 Pt(int i) => new Vector3(X[i], Y[i], Z[i]);


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> LodX { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> LodY { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> LodZ { get; set; }


        [XmlAttribute]
        public int Count { get; set; }
    }


    public class SpeedTreeRawObjectsObjectVertices
    {
        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> NormalX { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> NormalY { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> NormalZ { get; set; }

        public Vector3 Normal(int i) => new Vector3(NormalX[i], NormalY[i], NormalZ[i]);


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> BinormalX { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> BinormalY { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> BinormalZ { get; set; }

        public Vector3 Binormal(int i) => new Vector3(BinormalX[i], BinormalY[i], BinormalZ[i]);


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> TangentX { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> TangentY { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> TangentZ { get; set; }

        public Vector3 Tangent(int i) => new Vector3(TangentX[i], TangentY[i], TangentZ[i]);


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> TexcoordU { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> TexcoordV { get; set; }

        public Vector2 TexCoord(int i) => new Vector2(TexcoordU[i], 1 - TexcoordV[i]);


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> LightmapU { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> LightmapV { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> AO { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> Blend { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> VertexColorR { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> VertexColorG { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> VertexColorB { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> VertexColorA { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<int> GeometryType { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch1PosX { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch1PosY { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch1PosZ { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch1DirX { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch1DirY { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch1DirZ { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch1Weight { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch2PosX { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch2PosY { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch2PosZ { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch2DirX { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch2DirY { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch2DirZ { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindBranch2Weight { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> WindRippleWeight { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<int> BoneID { get; set; }


        [XmlAttribute]
        public int Count { get; set; }
    }

    public class SpeedTreeRawObjectsObjectTriangles
    {
        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<int> PointIndices { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<int> VertexIndices { get; set; }


        [XmlAttribute]
        public string Material { get; set; }


        [XmlAttribute]
        public int Count { get; set; }
    }
}