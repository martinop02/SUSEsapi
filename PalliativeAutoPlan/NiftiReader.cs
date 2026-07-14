using System;
using System.IO;
using System.IO.Compression;
using System.Text;

/*
    public static class NiftiReader
    {
        public static NiftiData LoadSegments(string filename)
        {
            Stream baseStream = OpenSeekableStream(filename);

            using (baseStream)
            using (var br = new BinaryReader(baseStream))
            {
                // --- header ---
                int sizeof_hdr = br.ReadInt32();
                br.ReadBytes(36); // data_type, db_name, extents, session_error, regular, dim_info

                short ndim = br.ReadInt16();
                short dimX = br.ReadInt16();
                short dimY = br.ReadInt16();
                short dimZ = br.ReadInt16();
                br.ReadBytes(16); // skip rest of dim[8]

                float intentP1 = br.ReadSingle();
                float intentP2 = br.ReadSingle();
                float intentP3 = br.ReadSingle();

                short intentCode = br.ReadInt16();
                // datatype (short) at offset 70
                br.BaseStream.Seek(70, SeekOrigin.Begin);
                short datatype = br.ReadInt16();

                // bitpix (short) at offset 72
                br.BaseStream.Seek(72, SeekOrigin.Begin);
                short bitpix = br.ReadInt16();   // bits per voxel
                short sliceStart = br.ReadInt16();
                br.ReadBytes(80); // pixdim, etc.

                // --- Read vox_offset correctly ---
                // vox_offset is a float at byte offset 108
                br.BaseStream.Seek(108, SeekOrigin.Begin);
                float vox_offset = br.ReadSingle();

                // --- Read description correctly ---
                // description is at byte offset 148 (80 chars)
                br.BaseStream.Seek(148, SeekOrigin.Begin);
                string descrip = Encoding.ASCII.GetString(br.ReadBytes(80)).TrimEnd('\0');

                // --- Seek to voxel data using vox_offset ---
                br.BaseStream.Seek((long)vox_offset, SeekOrigin.Begin);
                int nVoxels = dimX * dimY * dimZ;
                byte[,,] voxels;

                
                var bdata = new byte[dimX, dimY, dimZ];
                for (int k = 0; k < dimZ; k++)
                    for (int j = 0; j < dimY; j++)
                        for (int i = 0; i < dimX; i++)
                            bdata[i, j, k] = br.ReadByte();
                voxels = bdata;
                     
                return new NiftiData
                {
                    Data = voxels,
                    DimX = dimX,
                    DimY = dimY,
                    DimZ = dimZ,
                    Description = descrip,
                    DataType = datatype
                };
            }
        }

        private static Stream OpenSeekableStream(string filename)
        {
            if (filename.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                {
                    var ms = new MemoryStream();
                    gz.CopyTo(ms);
                    ms.Position = 0;
                    return ms;
                }
            }
            else
            {
                return new FileStream(filename, FileMode.Open, FileAccess.Read);
            }
        }
    }

    public class NiftiData
    {
        public byte[,,] Data { get; set; } // can be short[], float[], etc
        public int DimX { get; set; }
        public int DimY { get; set; }
        public int DimZ { get; set; }
        public string Description { get; set; }
        public int DataType { get; set; }
    }
*/
public static class NiftiReader
{
    public static NiftiData LoadSegments(string filename)
    {
        Stream baseStream = OpenSeekableStream(filename);
        try
        {
            using (var br = new BinaryReader(baseStream))
            {
                // --- read full header into buffer (348 bytes standard for NIfTI-1) ---
                byte[] header = br.ReadBytes(348);

                short dimX = BitConverter.ToInt16(header, 42);
                short dimY = BitConverter.ToInt16(header, 44);
                short dimZ = BitConverter.ToInt16(header, 46);
                short datatype = BitConverter.ToInt16(header, 70);
                float vox_offset = BitConverter.ToSingle(header, 108);
                string descrip = Encoding.ASCII.GetString(header, 148, 80).TrimEnd('\0');

                // --- move to voxel data ---
                br.BaseStream.Seek((long)vox_offset, SeekOrigin.Begin);

                int nVoxels = dimX * dimY * dimZ;
                byte[] raw = br.ReadBytes(nVoxels);   // read all voxel data at once

                // --- reshape into 3D ---
                bool empty = true;
                var voxels = new byte[dimX, dimY, dimZ];
                int idx = 0;
                for (int k = 0; k < dimZ; k++)
                    for (int j = 0; j < dimY; j++)
                        for (int i = 0; i < dimX; i++)
                        {
                            voxels[i, j, k] = raw[idx++];
                            if (voxels[i,j,k] != 0) empty = false;
                        }
                return new NiftiData
                {
                    Data = voxels,
                    DimX = dimX,
                    DimY = dimY,
                    DimZ = dimZ,
                    Description = descrip,
                    DataType = datatype,
                    Empty = empty
                };
            }
        }
        finally
        {
            // Important: BinaryReader.Dispose() will close baseStream,
            // so only dispose here if BinaryReader wasn't created
            if (baseStream != null)
                baseStream.Dispose();
        }
    }

    private static Stream OpenSeekableStream(string filename)
    {
        if (filename.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            var fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
            var gz = new GZipStream(fs, CompressionMode.Decompress);
            var ms = new MemoryStream();
            gz.CopyTo(ms);
            gz.Dispose();
            fs.Dispose();
            ms.Position = 0;
            return ms;
        }
        else
        {
            return new FileStream(filename, FileMode.Open, FileAccess.Read);
        }
    }
    
}
public class NiftiData
{
    public byte[,,] Data { get; set; } // can be short[], float[], etc
    public int DimX { get; set; }
    public int DimY { get; set; }
    public int DimZ { get; set; }
    public string Description { get; set; }
    public int DataType { get; set; }

    public bool Empty { get; set; }
}