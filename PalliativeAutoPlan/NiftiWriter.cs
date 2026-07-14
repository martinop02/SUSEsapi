using System;
using System.IO;
using System.Text;


    public static class NiftiWriter
    {
        /// <summary>
        /// Save a 3D short volume to NIfTI-1 format (single-file .nii) with sform affine.
        /// </summary>
        public static void SaveNifti(string filename, Nifti image)
        {
            short[,,] data = image.Data;

            double[,] affine = image.Affine;




            int dimX = data.GetLength(0);
            int dimY = data.GetLength(1);
            int dimZ = data.GetLength(2);

            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // --- NIFTI header (348 bytes) ---
                bw.Write(348);  // sizeof_hdr

                // data_type[10], db_name[18], extents, session_error, regular, dim_info
                bw.Write(new byte[36]);

                // dim[8]
                bw.Write((short)3);      // number of dimensions
                bw.Write((short)dimX);
                bw.Write((short)dimY);
                bw.Write((short)dimZ);
                for (int i = 0; i < 4; i++) bw.Write((short)1); // padding

                // intent
                bw.Write(0f); bw.Write(0f); bw.Write(0f);
                bw.Write((short)0);       // intent_code

                // datatype and bitpix
                bw.Write((short)4);       // DT_INT16
                bw.Write((short)16);      // bitpix
                bw.Write((short)0);       // slice_start

                // pixdim[8]
                bw.Write(0f);             // pixdim[0] (qfac, unused here)
                bw.Write((float)image.XRes);             // dx
                bw.Write((float)image.YRes);             // dy
                bw.Write((float)image.ZRes);             // dz
                bw.Write(0f); bw.Write(0f); bw.Write(0f); bw.Write(0f);

                // vox_offset (start of data)
                bw.Write(352f);

                // scaling
                bw.Write(1f);   // scl_slope
                bw.Write(0f);   // scl_inter

                // slice_end, slice_code
                bw.Write((short)0);
                bw.Write((byte)0);

                // xyzt_units: 2 = millimeters, 8 = seconds
                bw.Write((byte)2);

                // cal_max, cal_min, slice_duration, toffset
                bw.Write(0f); bw.Write(0f); bw.Write(0f); bw.Write(0f);

                // glmax, glmin
                bw.Write(0); bw.Write(0);

                // descrip[80]
                var descrip = Encoding.ASCII.GetBytes("Created by NiftiWriter");
                bw.Write(descrip);
                bw.Write(new byte[80 - descrip.Length]);

                // aux_file[24]
                bw.Write(new byte[24]);

                // qform_code / sform_code
                bw.Write((short)0);
                bw.Write((short)1);  // sform enabled

                // quaternion / qoffset (unused here)
                for (int i = 0; i < 6; i++) bw.Write(0f);

                // srow_x/y/z (affine)
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 4; col++)
                        bw.Write((float)affine[row, col]);

                // intent_name[16]
                bw.Write(new byte[16]);

                // magic "n+1\0"
                bw.Write(Encoding.ASCII.GetBytes("n+1\0"));

                // ensure header ends at 348
                long headerSize = fs.Position;
                if (headerSize < 352)
                    bw.Write(new byte[352 - headerSize]);

                // --- Voxel data (short) ---
                for (int k = 0; k < dimZ; k++)
                    for (int j = 0; j < dimY; j++)
                        for (int i = 0; i < dimX; i++)
                            bw.Write((short)data[i, j, k]);
            }
        }
    }
