using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace SE
{
    public static class MarchingCubes
    {
        public static List<Vector4> cubesGizmos = new List<Vector4>();

        public static List<Util.GridCell> TVDebugGridCells = new List<Util.GridCell>();
        public static List<Util.GridCell> TVDebugGridCellBounds = new List<Util.GridCell>();
        public static List<Vector3> DebugPoints = new List<Vector3>();


        // LOD byte
        // -x +x -y +z -z +z

        public static MCMesh PolygonizeArea(Vector3 min, byte lod, int resolution, sbyte[][][][] data)
        {
            MCMesh m = new MCMesh();

            int res1 = resolution + 1;
            int resm1 = resolution - 1;
            int resm2 = resolution - 2;

            cubesGizmos.Add(new Vector4(min.x + (resolution / 2), min.y + (resolution / 2), min.z + (resolution / 2), resolution));

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<int> triangles = new List<int>();

            ushort[] edges = new ushort[res1 * res1 * res1 * 3];


            Vector3Int begin = new Vector3Int(0, 0, 0);
            Vector3Int end = new Vector3Int(res1, res1, res1);

			System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
			sw.Start();

			if((lod & 1) == 1) begin.x += 1;
			if((lod & 2) == 2) end.x -= 1;
			
			if((lod & 4) == 4) begin.y += 1;
			if((lod & 8) == 8) end.y -= 1;
			
			if((lod & 16) == 16) begin.z += 1;
			if((lod & 32) == 32) end.z -= 1;

            CreateVertices(edges, begin, end, vertices, normals, res1, data);


			end -= Vector3Int.one;
			//if((lod & 1) == 1) begin.x += 1;


            Triangulate(edges, begin, end, triangles, resolution, data);

			//Debug.Log("Phase 1 of surface extraction took " + sw.ElapsedMilliseconds + " ms.");

			sw.Restart();

            GenerateTransitionCells(vertices, normals, triangles, resolution, data, lod);

			//Debug.Log("Phase 2 of surface extraction took " + sw.ElapsedMilliseconds + " ms.");
            //MCVT(vertices, triangles, normals, resolution, lod, data);

            m.Vertices = vertices;
            m.Triangles = triangles.ToArray();
            m.Normals = normals;

            return m;
        }

        private class MCVT_Cell
        {
            public ushort[] uniqueEdges; // max size of 54
        };

        public static readonly ushort REUSABLE_EDGES_PER_CELL = 16;

        public static void MCVT(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, int resolution, byte chunkLod, sbyte[][][][] data)
        {
            Debug.Assert(resolution % 2 == 0);

            int halfres = resolution / 2;

            //ushort[] reusedEdges = new ushort[REUSABLE_EDGES_PER_CELL * halfres * halfres];
            //MCVT_Cell[,] cellBuffer = new MCVT_Cell[resolution/2,resolution/2];
            //for(int i = 0; i < 2; i++) cellBuffer[i] = new MCVT_Cell[resolution/2, resolution/2];
            MCVT_Cell[,,] cells = new MCVT_Cell[halfres, halfres, halfres];

            ushort[,,,] cellReusedEdges = new ushort[halfres, halfres, halfres, 52];

            int hx = -1; int hy = -1; int hz = -1;

            for (int x = 0; x < resolution; x += 2)
            {
                hx = x / 2;
                for (int y = 0; y < resolution; y += 2)
                {
                    hy = y / 2;
                    for (int z = 0; z < resolution; z += 2)
                    {
                        hz = z / 2;
                        MCVT_Cell mcvtcell = new MCVT_Cell();

                        cells[hx, hy, hz] = mcvtcell;

                        byte lod = 0;

                        if (x == 0) lod |= 1;
                        if (x == resolution - 2) lod |= 2;
                        if (y == 0) lod |= 4;
                        if (y == resolution - 2) lod |= 8;
                        if (z == 0) lod |= 16;
                        if (z == resolution - 2) lod |= 32;

                        lod = (byte)(chunkLod & lod);
                        mcvtcell.uniqueEdges = new ushort[Tables.MCLodUniqueEdgesReuse[lod].Length];

                        Debug.Log("creating uniqueEdges array with length: " + Tables.MCLodUniqueEdgesReuse[lod].Length + " (lod " + lod + ")");

                        for (int edgeNum = 0; edgeNum < Tables.MCLodUniqueEdgesReuse[lod].Length; edgeNum++)
                        {
                            int longb = Tables.MCLodUniqueEdgesReuse[lod][edgeNum];
                            byte EdgeA = (byte)(longb & 63);
                            byte EdgeB = (byte)((longb >> 6) & 63);
                            byte ReuseCell = (byte)((longb >> 12) & 15);
                            byte ReuseIndex = (byte)((longb >> 16) & 63);
                            byte AlternateReuseIndex = (byte)((longb >> 22) & 63);

                            if (edgeNum == 0)
                            {
                                Debug.Log("EdgeA: " + EdgeA + ", EdgeB" + EdgeB + ", ReuseCell: " + ReuseCell + ", ReuseIndex: " + ReuseIndex + ", AlternateReuseIndex: " + AlternateReuseIndex);
                            }

                            bool reusing = false;
                            bool altreusing = false;
                            Vector3Int ReuseIndices = new Vector3Int(hx, hy, hz);

                            Vector3Int AlternateReuseIndices = new Vector3Int(hx, hy, hz);

                            bool altReuseExists = false;

                            if (ReuseCell != 0)
                            {
                                reusing = true;
                                if ((ReuseCell & 1) == 1)
                                {
                                    ReuseIndices.x -= 1;
                                    if ((ReuseCell & 2) == 2)
                                    {
                                        altReuseExists = true;
                                        AlternateReuseIndices.z -= 1;
                                    }
                                    else if ((ReuseCell & 4) == 4)
                                    {
                                        altReuseExists = true;
                                        AlternateReuseIndices.y -= 1;
                                    }
                                }
                                else if ((ReuseCell & 2) == 2)
                                {
                                    ReuseIndices.z -= 1;
                                    if ((ReuseCell & 4) == 4)
                                    {
                                        altReuseExists = true;
                                        AlternateReuseIndices.y -= 1;
                                    }
                                }
                                else if ((ReuseCell & 4) == 4)
                                {
                                    ReuseIndices.y -= 1;
                                }
                                if (ReuseIndices.x < 0 || ReuseIndices.y < 0 || ReuseIndices.z < 0)
                                {
                                    //Debug.Log("ee");
                                    reusing = false;
                                    altreusing = true;
                                    if (!altReuseExists || (AlternateReuseIndices.x < 0 || AlternateReuseIndices.y < 0 || AlternateReuseIndices.z < 0))
                                    {
                                        altreusing = false;
                                    }
                                }
                            }
                            if (reusing)
                            {
                                Debug.Log("Got here reuse cell" + ReuseCell);
                                mcvtcell.uniqueEdges[edgeNum] =
                                    cellReusedEdges[
                                        ReuseIndices.x,
                                        ReuseIndices.y,
                                        ReuseIndices.z,
                                        ReuseIndex
                                    ];
                            }
                            else if (altreusing)
                            {
                                mcvtcell.uniqueEdges[edgeNum] =
                                cellReusedEdges[
                                    AlternateReuseIndices.x,
                                    AlternateReuseIndices.y,
                                    AlternateReuseIndices.z,
                                    AlternateReuseIndex
                                ];
                            }
                            else
                            {
                                Vector3 A = ByteToVector3(EdgeA) + new Vector3(x, y, z);
                                Vector3 B = ByteToVector3(EdgeB) + new Vector3(x, y, z);
                                sbyte d1 = data[(int)A.x][(int)A.y][(int)A.z][0];
                                sbyte d2 = data[(int)B.x][(int)B.y][(int)B.z][0];
                                if ((d1 & 256) != (d2 & 256))
                                {
                                    Util.Point A_ = new Util.Point();
                                    A_.density = d1; A_.position = A;
                                    Util.Point B_ = new Util.Point();
                                    B_.density = d2; B_.position = B;

                                    Vector3 lerped = UtilFuncs.Lerp(0, A_, B_);
                                    mcvtcell.uniqueEdges[edgeNum] = (ushort)vertices.Count;
                                    vertices.Add(lerped);
                                }
                            }

                        }

                        //Debug.Log("Cell vertex count: " + mcvtcell.uniqueEdges.Length);

                        for (int i = 0; i < Tables.MCLodEdgeToReID[lod].GetLength(0); i++)
                        {
                            byte edgeNum = Tables.MCLodEdgeToReID[lod][i, 0];
                            byte reId = Tables.MCLodEdgeToReID[lod][i, 1];

                            if(edgeNum > mcvtcell.uniqueEdges.Length) {
                                Debug.LogError("ERROR: edgeNum > mcvtcell.uniqueEdges.Length");
                                Debug.LogError("EdgeNum: " + edgeNum + ", lod: " + lod + ", Tables.UniqueEdges[lod].length: " + Tables.MCLodUniqueEdges[lod].Length + ", uniqueEdges length: " + mcvtcell.uniqueEdges.Length);
                            }

                            ushort id = mcvtcell.uniqueEdges[edgeNum];

                            cellReusedEdges[hx, hy, hz, reId] = id;
                        }

                        Util.GridCell tvCellBounds = new Util.GridCell();
                        tvCellBounds.points = new Util.Point[8];
                        for (int i = 0; i < 8; i++)
                        {
                            tvCellBounds.points[i].position = new Vector3(x, y, z) + Tables.CellOffsets[i] * 2;
                            //DebugPoints.Add(tvCellBounds.points[i].position);
                        }
                        TVDebugGridCellBounds.Add(tvCellBounds);

                        byte[][] offsets = Tables.MCLodTable[lod];

                        Debug.Log("offset length: " + offsets.Length);

                        if (offsets.Length > 0)
                        {
                            for (int i = 0; i < offsets.Length; i++)
                            {
                                Util.GridCell cell = new Util.GridCell();
                                cell.points = new Util.Point[8];

                                string strOffs = "Cell " + i + " offsets: ";

                                for (int j = 0; j < 8; j++)
                                {
                                    cell.points[j] = new Util.Point();
                                    Vector3 pos = new Vector3(x + 1, y + 1, z + 1);
                                    byte offset = offsets[i][j];
                                    if ((offset & 1) == 1) pos.x -= 1;
                                    if ((offset & 2) == 2) pos.x += 1;
                                    if ((offset & 4) == 4) pos.y -= 1;
                                    if ((offset & 8) == 8) pos.y += 1;
                                    if ((offset & 16) == 16) pos.z -= 1;
                                    if ((offset & 32) == 32) pos.z += 1;

                                    strOffs += pos + " (" + offset + "), ";

                                    cell.points[j].position = pos;
                                    cell.points[j].density = (float)data[((int)pos.x)][((int)pos.y)][((int)pos.z)][0];

                                }

                                //Debug.Log(strOffs);
                                { // Polyganise
                                    float isovalue = 0;
                                    Vector3[] vertlist = new Vector3[12];

                                    int iz;
                                    int cubeindex;

                                    cubeindex = 0;
                                    if (cell.points[0].density < isovalue) cubeindex |= 1;
                                    if (cell.points[1].density < isovalue) cubeindex |= 2;
                                    if (cell.points[2].density < isovalue) cubeindex |= 4;
                                    if (cell.points[3].density < isovalue) cubeindex |= 8;
                                    if (cell.points[4].density < isovalue) cubeindex |= 16;
                                    if (cell.points[5].density < isovalue) cubeindex |= 32;
                                    if (cell.points[6].density < isovalue) cubeindex |= 64;
                                    if (cell.points[7].density < isovalue) cubeindex |= 128;

                                    // Cube is entirely in/out of the surface 
                                    if (Tables.edgeTable[cubeindex] != 0)
                                    {
                                        /*int[,] edgepairs = { {0, 1}, {1, 2}, {2, 3}, {3, 0}, {4, 5}, {5, 6}, {6, 7}, {7, 4}, {0, 4}, {1, 5}, {2, 6}, {3, 7} };

                                        int andEd = 1;

                                        for(int ja = 0; ja < 12; ja++) {
                                            if((Tables.edgeTable[cubeindex] & andEd) == andEd) {
                                                byte id = Tables.MCLodEdgeMapping[lod][i][ja];
                                                ushort vertId = mcvtcell.uniqueEdges[id];
                                                Util.Point A_ = cell.points[edgepairs[ja,0]];
                                                Util.Point B_ = cell.points[edgepairs[ja,1]];

                                                vertlist[ja] = UtilFuncs.Lerp(isovalue, A_, B_);
                                            }
                                            andEd *= 2;
                                        }*/

                                        // Create the triangle
                                        for (iz = 0; Tables.triTable[cubeindex][iz] != -1; iz++)
                                        {
                                            int edgeNum = Tables.triTable[cubeindex][iz];

                                            byte id = SE.Tables.MCLodEdgeMappingTable[lod][i, edgeNum];
                                            ushort vertId = mcvtcell.uniqueEdges[id];

                                            triangles.Add(vertId);
                                        }
                                    }

                                }
                                //Polyganise(cell, vertices, triangles, 0f);
                                TVDebugGridCells.Add(cell);

                            }
                        }
                    }
                }
            }
            HighlightDuplicateVertices(vertices);
        }

        public static void FindEdgeId(byte lod, Vector3 A, Vector3 B)
        {
            //dim0: cell#
            //dim1: edge# (0-12)
            //dim2: edge# (0-1)

            // first step: get all the unique edges of the lod cell and number them
            //Vector3[][][] tempTable = new Vector3[Tables.MCLodTable[lod].Length][][];

            List<Vector3[]> UniqueEdges = new List<Vector3[]>();

            byte[][] tempOffsetTable = Tables.MCLodTable[lod];

            for (int i = 0; i < tempOffsetTable.Length; i++)
            {
                for (int j = 0; j < 12; j++)
                {
                    int a = Tables.edgePairs[j, 0];
                    int b = Tables.edgePairs[j, 1];

                    Vector3 A_ = ByteToVector3(tempOffsetTable[i][a]);
                    Vector3 B_ = ByteToVector3(tempOffsetTable[i][b]);

                    bool unique = true;

                    foreach (Vector3[] pair in UniqueEdges)
                    {
                        if (pair[0] == A_ && pair[1] == B_) { unique = false; break; }
                        if (pair[0] == B_ && pair[1] == A_) { unique = false; break; }
                    }

                    if (unique)
                    {
                        Vector3[] pair = { A_, B_ };
                        UniqueEdges.Add(pair);
                    }

                }
            }



        }

        public static void HighlightDuplicateVertices(List<Vector3> vertices)
        {
            for (int vAN = 0; vAN < vertices.Count; vAN++)
            {
                Vector3 vA = vertices[vAN];
                for (int vBN = 0; vBN < vertices.Count; vBN++)
                {
                    if (vBN == vAN) continue;
                    Vector3 vB = vertices[vBN];
                    if (Vector3.Distance(vA, vB) < 0.05f && !DebugPoints.Contains(vA) && !DebugPoints.Contains(vB))
                    {
                        DebugPoints.Add(vA);
                    }
                }
            }
        }

        public static void GenerateUniqueEdgeLists()
        {
            Vector3[][][] table = new Vector3[63][][];

            for (int lod = 0; lod < 63; lod++)
            {

                List<Vector3[]> UniqueEdges = new List<Vector3[]>();

                byte[][] tempOffsetTable = Tables.MCLodTable[lod];

                for (int i = 0; i < tempOffsetTable.Length; i++)
                {
                    for (int j = 0; j < 12; j++)
                    {
                        int a = Tables.edgePairs[j, 0];
                        int b = Tables.edgePairs[j, 1];

                        Vector3 A_ = ByteToVector3(tempOffsetTable[i][a]);
                        Vector3 B_ = ByteToVector3(tempOffsetTable[i][b]);

                        bool unique = true;

                        foreach (Vector3[] pair in UniqueEdges)
                        {
                            if (pair[0] == A_ && pair[1] == B_) { unique = false; break; }
                            if (pair[0] == B_ && pair[1] == A_) { unique = false; break; }
                        }

                        if (unique)
                        {
                            Vector3[] pair = { A_, B_ };
                            UniqueEdges.Add(pair);
                        }

                    }
                }

                table[lod] = UniqueEdges.ToArray();
            }
        }

        public static Vector3 ByteToVector3(byte e)
        {
            Vector3 pos = new Vector3(1, 1, 1);
            byte offset = e;
            if ((offset & 1) == 1) pos.x -= 1;
            if ((offset & 2) == 2) pos.x += 1;
            if ((offset & 4) == 4) pos.y -= 1;
            if ((offset & 8) == 8) pos.y += 1;
            if ((offset & 16) == 16) pos.z -= 1;
            if ((offset & 32) == 32) pos.z += 1;
            return pos;
        }

        public static void CreateVertices(ushort[] edges, Vector3Int begin, Vector3Int end, List<Vector3> vertices, List<Vector3> normals, int res1, sbyte[][][][] data)
        {
			//Debug.Log("CreateVertices called with begin " + begin + ", end: " + end);

            int edgeNum = 0;
            ushort vertNum = 0;
            sbyte density1, density2;

            int res1_3 = res1 * 3;
            int res1_2_3 = res1 * res1 * 3;

            for (int x = begin.x; x < end.x; x++)
            {
                for (int y = begin.y; y < end.y; y++)
                {
                    for (int z = begin.z; z < end.z; z++, edgeNum += 3)
                    {
                        edgeNum = GetEdge3D(x, y, z, 0, res1);
                        density1 = data[x][y][z][0];

                        if (density1 == 0)
                        {
                            edges[edgeNum] = vertNum;
                            edges[edgeNum + 1] = vertNum;
                            edges[edgeNum + 2] = vertNum;
                            vertNum++;
                            normals.Add(new Vector3(data[x][y][z][1] / 127f, data[x][y][z][2] / 127f, data[x][y][z][3] / 127f));
                            vertices.Add(new Vector3(x, y, z));
                            continue;
                        }
                        if (y >= begin.y + 1)
                        {
                            density2 = data[x][y - 1][z][0];
                            if ((density1 & 256) != (density2 & 256))
                            {
                                if (density2 == 0)
                                {
                                    edges[edgeNum] = edges[edgeNum - res1_3];
                                }
                                else
                                {
                                    edges[edgeNum] = vertNum;
                                    vertNum++;
                                    normals.Add(LerpN(density1, density2,
                                        data[x][y][z][1], data[x][y][z][2], data[x][y][z][3],
                                        data[x][y - 1][z][1], data[x][y - 1][z][2], data[x][y - 1][z][3]));
                                    vertices.Add(Lerp(density1, density2, x, y, z, x, y - 1, z));
                                }
                            }
                        }
                        if (x >= begin.x + 1)
                        {
                            density2 = data[x - 1][y][z][0];
                            if ((density1 & 256) != (density2 & 256))
                            {
                                if (density2 == 0)
                                {
                                    edges[edgeNum + 1] = edges[edgeNum - res1_2_3];
                                }
                                else
                                {
                                    edges[edgeNum + 1] = vertNum;
                                    vertNum++;
                                    normals.Add(LerpN(density1, density2,
                                        data[x][y][z][1], data[x][y][z][2], data[x][y][z][3],
                                        data[x - 1][y][z][1], data[x - 1][y][z][2], data[x - 1][y][z][3]));
                                    vertices.Add(Lerp(density1, density2, x, y, z, x - 1, y, z));
                                }
                            }
                        }
                        if (z >= begin.z + 1)
                        {
                            density2 = data[x][y][z - 1][0];
                            if ((density1 & 256) != (density2 & 256))
                            {
                                if (density2 == 0)
                                {
                                    edges[edgeNum + 2] = edges[edgeNum - 3];
                                }
                                else
                                {
                                    edges[edgeNum + 2] = vertNum;
                                    vertNum++;
                                    normals.Add(LerpN(density1, density2,
                                        data[x][y][z][1], data[x][y][z][2], data[x][y][z][3],
                                        data[x][y][z - 1][1], data[x][y][z - 1][2], data[x][y][z - 1][3]));
                                    vertices.Add(Lerp(density1, density2, x, y, z, x, y, z - 1));
                                }
                            }
                        }
                    }
                }
            }
        }
        public static void Triangulate(ushort[] edges, Vector3Int begin, Vector3Int end, List<int> triangles, int resolution, sbyte[][][][] data)
        {
            sbyte[] densities = new sbyte[8];
            int i, j;
            int mcEdge;

            int res1 = resolution + 1;
            int res1_2 = res1 * res1;

            int t1, t2, t3;

            for (int x = begin.x; x < end.x; x++)
            {
                for (int y = begin.y; y < end.y; y++)
                {
                    for (int z = begin.z; z < end.z; z++)
                    {
                        byte caseCode = 0;

                        densities[0] = data[x][y][z + 1][0];
                        densities[1] = data[x + 1][y][z + 1][0];
                        densities[2] = data[x + 1][y][z][0];
                        densities[3] = data[x][y][z][0];
                        densities[4] = data[x][y + 1][z + 1][0];
                        densities[5] = data[x + 1][y + 1][z + 1][0];
                        densities[6] = data[x + 1][y + 1][z][0];
                        densities[7] = data[x][y + 1][z][0];

                        if (densities[0] < 0) caseCode |= 1;
                        if (densities[1] < 0) caseCode |= 2;
                        if (densities[2] < 0) caseCode |= 4;
                        if (densities[3] < 0) caseCode |= 8;
                        if (densities[4] < 0) caseCode |= 16;
                        if (densities[5] < 0) caseCode |= 32;
                        if (densities[6] < 0) caseCode |= 64;
                        if (densities[7] < 0) caseCode |= 128;

                        if (caseCode == 0 || caseCode == 255) continue;

                        for (i = 0; Tables.triTable[caseCode][i] != -1; i += 3)
                        {
                            mcEdge = Tables.triTable[caseCode][i];
                            t1 = edges[3 * (
                                ((x + Tables.MCEdgeToEdgeOffset[mcEdge, 0]) * res1_2) +
                                ((y + Tables.MCEdgeToEdgeOffset[mcEdge, 1]) * res1) +
                                   z + Tables.MCEdgeToEdgeOffset[mcEdge, 2]) +
                                       Tables.MCEdgeToEdgeOffset[mcEdge, 3]];

                            mcEdge = Tables.triTable[caseCode][i + 1];
                            t2 = edges[3 * (
                                ((x + Tables.MCEdgeToEdgeOffset[mcEdge, 0]) * res1_2) +
                                ((y + Tables.MCEdgeToEdgeOffset[mcEdge, 1]) * res1) +
                                   z + Tables.MCEdgeToEdgeOffset[mcEdge, 2]) +
                                       Tables.MCEdgeToEdgeOffset[mcEdge, 3]];

                            mcEdge = Tables.triTable[caseCode][i + 2];
                            t3 = edges[3 * (
                                ((x + Tables.MCEdgeToEdgeOffset[mcEdge, 0]) * res1_2) +
                                ((y + Tables.MCEdgeToEdgeOffset[mcEdge, 1]) * res1) +
                                   z + Tables.MCEdgeToEdgeOffset[mcEdge, 2]) +
                                       Tables.MCEdgeToEdgeOffset[mcEdge, 3]];

                            if (t1 != t2 && t2 != t3 && t1 != t3)
                            {
                                triangles.Add(t1);
                                triangles.Add(t2);
                                triangles.Add(t3);
                            }
                        }

                    }
                }
            }
        }

        unsafe private struct Cell
        {
            fixed int vertIDs[12];
        };

        public static void GenerateTransitionCells(List<Vector3> vertices, List<Vector3> normals, List<int> triangles, int resolution, sbyte[][][][] data, byte lod)
        {
			int resm2 = resolution - 2;
            for (int x = 0; x < resolution; x += 2)
            {
                for (int y = 0; y < resolution; y += 2)
                {
                    for (int z = 0; z < resolution; z += 2)
                    {
						bool isMinimal = false;
						if(x == 0 || y == 0 || z == 0) {
							isMinimal = true;
						}
						bool isMaximal = false;
						if(x == resm2 || y == resm2 || z == resm2) {
							isMaximal = true;
						}

						if(!isMinimal && !isMaximal) {
							continue;
						}

                        byte cellLod = 0;

                        if (x == 0) cellLod |= 1;
                        if (x == resolution - 2) cellLod |= 2;
                        if (y == 0) cellLod |= 4;
                        if (y == resolution - 2) cellLod |= 8;
                        if (z == 0) cellLod |= 16;
                        if (z == resolution - 2) cellLod |= 32;

                        cellLod = (byte)(lod & cellLod);

                        /*Util.GridCell tvCellBounds = new Util.GridCell();
                        tvCellBounds.points = new Util.Point[8];
                        for (int i = 0; i < 8; i++)
                        {
                            tvCellBounds.points[i].position = new Vector3(x, y, z) + Tables.CellOffsets[i] * 2;
                            //DebugPoints.Add(tvCellBounds.points[i].position);
                        }
                        TVDebugGridCellBounds.Add(tvCellBounds);*/

                        byte[][] offsets = Tables.MCLodTable[cellLod];

                        if (offsets.Length > 0)
                        {
                            for (int i = 0; i < offsets.Length; i++)
                            {
                                //Util.GridCell cell = new Util.GridCell();
                                //cell.points = new Util.Point[8];

                                //string strOffs = "Cell " + i + " offsets: ";

								//Vector3Int[] cellOffsets = new Vector3Int[8];
								sbyte[] densities = new sbyte[8];
								Vector3Int[] points = new Vector3Int[8];
                                for (int j = 0; j < 8; j++)
                                {
                                    Vector3Int pos = new Vector3Int(x + 1, y + 1, z + 1);
                                    byte offset = offsets[i][j];
                                    if ((offset & 1) == 1) pos.x -= 1;
                                    if ((offset & 2) == 2) pos.x += 1;
                                    if ((offset & 4) == 4) pos.y -= 1;
                                    if ((offset & 8) == 8) pos.y += 1;
                                    if ((offset & 16) == 16) pos.z -= 1;
                                    if ((offset & 32) == 32) pos.z += 1;

									points[j] = pos;
									//cell.points[j] = new Util.Point();
									//cell.points[j].position = pos;
									densities[j] = data[pos.x][pos.y][pos.z][0];
                                    //cell.points[j].position = pos;
                                    //cell.points[j].density = (float)data[((int)pos.x)][((int)pos.y)][((int)pos.z)][0];
                                }


								//Vector3[] vertlist = new Vector3[12];
								byte caseCode = 0;

								if (densities[0] < 0) caseCode |= 1;
								if (densities[1] < 0) caseCode |= 2;
								if (densities[2] < 0) caseCode |= 4;
								if (densities[3] < 0) caseCode |= 8;
								if (densities[4] < 0) caseCode |= 16;
								if (densities[5] < 0) caseCode |= 32;
								if (densities[6] < 0) caseCode |= 64;
								if (densities[7] < 0) caseCode |= 128;

								int vertCount = vertices.Count;
								int[] vertList = new int[12];

								if ((Tables.edgeTable[caseCode] & 1) == 1) {
									int a = 0, b = 1;
									vertices.Add(Lerp2(densities[a], densities[b], points[a], points[b]));

									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[0] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 2) == 2) {
									vertices.Add(Lerp2(densities[1], densities[2], points[1], points[2]));

									int a = 1, b = 2;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));
									
									vertList[1] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 4) == 4) {
									vertices.Add(Lerp2(densities[2], densities[3], points[2], points[3]));

									int a = 2, b = 3;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[2] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 8) == 8) {
									vertices.Add(Lerp2(densities[3], densities[0], points[3], points[0]));
									
									int a = 3, b = 0;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[3] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 16) == 16) {
									vertices.Add(Lerp2(densities[4], densities[5], points[4], points[5]));

									int a = 4, b = 5;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[4] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 32) == 32) {
									vertices.Add(Lerp2(densities[5], densities[6], points[5], points[6]));

									int a = 5, b = 6;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[5] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 64) == 64) {
									vertices.Add(Lerp2(densities[6], densities[7], points[6], points[7]));

									int a = 6, b = 7;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[6] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 128) == 128) {
									vertices.Add(Lerp2(densities[7], densities[4], points[7], points[4]));

									int a = 7, b = 4;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[7] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 256) == 256) {
									vertices.Add(Lerp2(densities[0], densities[4], points[0], points[4]));
									int a = 0, b = 4;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[8] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 512) == 512) {
									vertices.Add(Lerp2(densities[1], densities[5], points[1], points[5]));
									int a = 1, b = 5;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[9] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 1024) == 1024) {
									vertices.Add(Lerp2(densities[2], densities[6], points[2], points[6]));
									int a = 2, b = 6;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[b].x][points[b].y][points[b].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[10] = vertCount++;
								}
								if ((Tables.edgeTable[caseCode] & 2048) == 2048) {
									vertices.Add(Lerp2(densities[3], densities[7], points[3], points[7]));

									int a = 3, b = 7;
									sbyte[] data1 = data[points[a].x][points[a].y][points[a].z];
									sbyte[] data2 = data[points[a].x][points[a].y][points[a].z];

                                    normals.Add(LerpN(densities[a], densities[b],
                                        data1[1], data1[2], data1[3], data2[1], data2[2], data2[3]));

									vertList[11] = vertCount++;
								}


								if (caseCode == 0 || caseCode == 255) continue;
           						int mcEdge;
								int t1, t2, t3;



								for (int j = 0; Tables.triTable[caseCode][j] != -1; j++)
								{
									triangles.Add(vertList[Tables.triTable[caseCode][j]]);
								}


                                //Debug.Log(strOffs);
                                //TVDebugGridCells.Add(cell);

                            }
                        }



                        //if(cellLod) 
                    }
                }
            }
        }

        public static void DrawGizmos()
        {
            Gizmos.color = Color.white;
            foreach (Vector4 cube in cubesGizmos)
            {
                UnityEngine.Gizmos.DrawWireCube(new Vector3(cube.x, cube.y, cube.z), Vector3.one * cube.w);
            }

            Gizmos.color = Color.red;
            DrawCubeGizmos();

            Gizmos.color = Color.blue;
            foreach (Vector3 point in DebugPoints)
            {
                Gizmos.DrawSphere(point, 0.15f);
            }

            return;
        }

        public static float mu;
        public static Vector3 Lerp(float density1, float density2, float x1, float y1, float z1, float x2, float y2, float z2)
        {
            if (density1 < 0.00001f && density1 > -0.00001f)
            {
                return new Vector3(x1, y1, z1);
            }
            if (density2 < 0.00001f && density2 > -0.00001f)
            {
                return new Vector3(x2, y2, z2);
            }
            /*if(Mathf.Abs(density1 - density2) < 0.00001f) {
                return new Vector3(x2, y2, z2);
            }*/

            mu = Mathf.Round((density1) / (density1 - density2) * 256) / 256.0f;

            return new Vector3(x1 + mu * (x2 - x1), y1 + mu * (y2 - y1), z1 + mu * (z2 - z1));
        }

        public static Vector3 Lerp2(float density1, float density2, Vector3 A, Vector3 B)
        {
            if (density1 < 0.00001f && density1 > -0.00001f)
            {
                return new Vector3(A.x, A.y, A.z);
            }
            if (density2 < 0.00001f && density2 > -0.00001f)
            {
                return new Vector3(B.x, B.y, B.z);
            }
            /*if(Mathf.Abs(density1 - density2) < 0.00001f) {
                return new Vector3(x2, y2, z2);
            }*/

            mu = Mathf.Round((density1) / (density1 - density2) * 256) / 256.0f;

            return new Vector3(A.x + mu * (B.x - A.x), A.y + mu * (B.y - A.y), A.z + mu * (B.z - A.z));
        }

        public static Vector3 LerpN(float density1, float density2, float n1x, float n1y, float n1z, float n2x, float n2y, float n2z)
        {
            mu = Mathf.Round((density1) / (density1 - density2) * 256f) / 256f;

            return new Vector3(n1x / 127f + mu * (n2x / 127f - n1x / 127f), n1y / 127f + mu * (n2y / 127f - n1y / 127f), n1z / 127f + mu * (n2z / 127f - n1z / 127f));
        }

        public static int GetEdge3D(int x, int y, int z, int edgeNum, int res)
        {
            return (3 * ((x * res * res) + (y * res) + z)) + edgeNum;
        }

        public static void DrawCubeGizmos()
        {
            int nGridcells = 0;
            foreach (Util.GridCell cell in TVDebugGridCells)
            {
                nGridcells++;
                DrawGridCell(cell);
            }
            //Debug.Log("Drew " + nGridcells + " gridcells.");
            foreach (Util.GridCell cell in TVDebugGridCellBounds)
            {
                DrawGridCell(cell);
            }
        }

        public static void DrawGridCell(Util.GridCell cell)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector3 vert1 = cell.points[Tables.edgePairs[i, 0]].position;
                Vector3 vert2 = cell.points[Tables.edgePairs[i, 1]].position;
                Gizmos.DrawLine(vert1, vert2);
            }
        }

        public static Vector4 ReverseGetEdge3D(int edgeNum, int res)
        {
            int res3 = res * res * res;
            int res2 = res * res;

            int w = edgeNum % 3;

            int edgeDiv3 = (edgeNum - w) / 3;

            int x = edgeDiv3 / res2;
            int y = (edgeDiv3 - x * res2) / res;
            int z = (edgeDiv3 - ((x * res2) + (y * res)));

            return new Vector4(x, y, z, w);
        }

        public static readonly int[,] EdgeOffsets = {
            {0, -1, 0}, {-1, 0, 0}, {0, 0, -1}
        };

        public static void Polyganise(Util.GridCell cell, List<Vector3> vertices, List<int> triangles, float isovalue)
        {
            Vector3[] vertlist = new Vector3[12];

            int i, ntriang;
            int cubeindex;

            cubeindex = 0;
            if (cell.points[0].density < isovalue) cubeindex |= 1;
            if (cell.points[1].density < isovalue) cubeindex |= 2;
            if (cell.points[2].density < isovalue) cubeindex |= 4;
            if (cell.points[3].density < isovalue) cubeindex |= 8;
            if (cell.points[4].density < isovalue) cubeindex |= 16;
            if (cell.points[5].density < isovalue) cubeindex |= 32;
            if (cell.points[6].density < isovalue) cubeindex |= 64;
            if (cell.points[7].density < isovalue) cubeindex |= 128;

            /* Cube is entirely in/out of the surface */
            if (Tables.edgeTable[cubeindex] == 0)
            {
                return;
            }

            /* Find the vertices where the surface intersects the cube */
            if ((Tables.edgeTable[cubeindex] & 1) == 1)
                vertlist[0] = UtilFuncs.Lerp(isovalue, cell.points[0], cell.points[1]);
            if ((Tables.edgeTable[cubeindex] & 2) == 2)
                vertlist[1] = UtilFuncs.Lerp(isovalue, cell.points[1], cell.points[2]);
            if ((Tables.edgeTable[cubeindex] & 4) == 4)
                vertlist[2] = UtilFuncs.Lerp(isovalue, cell.points[2], cell.points[3]);
            if ((Tables.edgeTable[cubeindex] & 8) == 8)
                vertlist[3] = UtilFuncs.Lerp(isovalue, cell.points[3], cell.points[0]);
            if ((Tables.edgeTable[cubeindex] & 16) == 16)
                vertlist[4] = UtilFuncs.Lerp(isovalue, cell.points[4], cell.points[5]);
            if ((Tables.edgeTable[cubeindex] & 32) == 32)
                vertlist[5] = UtilFuncs.Lerp(isovalue, cell.points[5], cell.points[6]);
            if ((Tables.edgeTable[cubeindex] & 64) == 64)
                vertlist[6] = UtilFuncs.Lerp(isovalue, cell.points[6], cell.points[7]);
            if ((Tables.edgeTable[cubeindex] & 128) == 128)
                vertlist[7] = UtilFuncs.Lerp(isovalue, cell.points[7], cell.points[4]);
            if ((Tables.edgeTable[cubeindex] & 256) == 256)
                vertlist[8] = UtilFuncs.Lerp(isovalue, cell.points[0], cell.points[4]);
            if ((Tables.edgeTable[cubeindex] & 512) == 512)
                vertlist[9] = UtilFuncs.Lerp(isovalue, cell.points[1], cell.points[5]);
            if ((Tables.edgeTable[cubeindex] & 1024) == 1024)
                vertlist[10] = UtilFuncs.Lerp(isovalue, cell.points[2], cell.points[6]);
            if ((Tables.edgeTable[cubeindex] & 2048) == 2048)
                vertlist[11] = UtilFuncs.Lerp(isovalue, cell.points[3], cell.points[7]);

            /* Create the triangle */
            for (i = 0; Tables.triTable[cubeindex][i] != -1; i++)
            {
                vertices.Add(vertlist[Tables.triTable[cubeindex][i]]);
                triangles.Add(vertices.Count - 1);
            }
        }

    }

    public class Edge
    {
        public Vector3 point1;
        public Vector3 point2;
        public Vector3 isoVertex;
    }
}
