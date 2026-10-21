namespace Fisher;

public class Native : Attribute { }

public class Guid : Attribute
{
    public long Value;
}

public class Divergent : Attribute { }

public class Ephemeral : Attribute { }

public class Entrypoint : Attribute
{
    public int Revision { get; set; }
    public int BranchID { get; set; }
    public int BranchRevision { get; set; }

    public Entrypoint(int revision = 626, int branchID = 19524, int branchRevision = 23)
    {
        Revision = revision;
        BranchID = branchID;
        BranchRevision = branchRevision;
    }
}

public class TypeNameAttribute : Attribute
{
    public string Name { get; }
    public TypeNameAttribute(string name) { Name = name; }
}

public struct Vec2
{
    public float x;
    public float y;

    public Vec2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }
}

public struct Vec3
{
    public float x;
    public float y;
    public float z;

    public Vec3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public Vec3(float x, float y)
    {
        this.x = x;
        this.y = y;
        this.z = 0;
    }
}

public struct Vec4
{
    public float x;
    public float y;
    public float z;
    public float w;

    public Vec4(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    public Vec4(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = 0;
    }

    public Vec4(float x, float y)
    {
        this.x = x;
        this.y = y;
        this.z = 0;
        this.w = 0;
    }
}

public struct Matrix4x4
{
    public float m00, m01, m02, m03;
    public float m10, m11, m12, m13;
    public float m20, m21, m22, m23;
    public float m30, m31, m32, m33;

    public float xx, xy, xz, xw;
    public float yx, yy, yz, yw;
    public float zx, zy, zz, zw;
    public float wx, wy, wz, ww;

    public Vec4 rX, rY, rZ, rW;
    public Vec4 cX, cY, cZ, cW;
    public Vec4 rx, ry, rz, rw;
    public Vec4 cx, cy, cz, cw;
    public Vec4 row0, row1, row2, row3;
    public Vec4 col0, col1, col2, col3;
}