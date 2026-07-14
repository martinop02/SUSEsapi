using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using Image = VMS.TPS.Common.Model.API.Image;

public class Nifti
{
    // Voxel data (HU values for CT, float or short typically)
    public short[,,] Data { get; set; }   // e.g. short[,,] or float[,,]

    // Dimensions
    public int DimX { get; set; }
    public int DimY { get; set; }
    public int DimZ { get; set; }

    // Origin in patient coordinates (mm)
    public double[] Origin { get; set; } = new double[3];

    // Direction cosines for each axis
    public double[] XDirection { get; set; } = new double[3];
    public double[] YDirection { get; set; } = new double[3];
    public double[] ZDirection { get; set; } = new double[3];

    // Spacing (mm per voxel)
    public double XRes { get; set; }
    public double YRes { get; set; }
    public double ZRes { get; set; }

    // Affine matrix (computed on demand)
    public double[,] Affine
    {
        get
        {
            return new double[,]
            {
                    { XDirection[0]*XRes, YDirection[0]*YRes, ZDirection[0]*ZRes, Origin[0] },
                    { XDirection[1]*XRes, YDirection[1]*YRes, ZDirection[1]*ZRes, Origin[1] },
                    { XDirection[2]*XRes, YDirection[2]*YRes, ZDirection[2]*ZRes, Origin[2] },
                    { 0,                  0,                  0,                  1 }
            };
        }
    }

    // Constructor
    public Nifti(Image img)
    {


        this.DimX = img.XSize;
        this.DimY = img.YSize;
        this.DimZ = img.ZSize;

        this.Origin = VVectToDouble(img.Origin);

        this.XDirection = VVectToDouble(img.XDirection);
        this.YDirection = VVectToDouble(img.YDirection);
        this.ZDirection = VVectToDouble(img.ZDirection);

        this.XRes = img.XRes;
        this.YRes = img.YRes;
        this.ZRes = img.ZRes;

        this.Data = GetImageData(img);




    }

    public double[] VVectToDouble(VVector vect)
    {
        double[] doub = new double[3];

        doub[0] = vect.x;
        doub[1] = vect.y;
        doub[2] = vect.z;

        return doub;
    }
    /*
    public short[,,] GetImageData(Image img)
    {

        short[,,] data = new short[DimX, DimY, DimZ];

        int[,] slice = new int[DimX, DimY];
        Console.WriteLine("Creating Nifti...");
        for (int k = 0; k < DimZ; k++)
        {
            img.GetVoxels(k, slice);
            for (int i = 0; i < DimX; i++)
            {
                for (int j = 0; j < DimY; j++)
                {
                    data[DimX - 1 - i, DimY - 1 - j, k] = (short)Math.Round(img.VoxelToDisplayValue(slice[i, j]));
                }
            }
            Console.WriteLine($"Slice {k}/{DimZ} done.");
        }
        Console.WriteLine("Nifti done.");
        return data;

    }*/
    public short[,,] GetImageData(Image img)
    {
        short[] data = new short[DimX * DimY * DimZ];
        int[,] slice = new int[DimX, DimY];

        Console.WriteLine("Creating Nifti...");
        /*
        unsafe
        {
            fixed (short* pData = data)
            {
                for (int k = 0; k < DimZ; k++)
                {
                    img.GetVoxels(k, slice);

                    for (int i = 0; i < DimX; i++)
                    {
                        for (int j = 0; j < DimY; j++)
                        {
                            int flippedI = DimX - 1 - i;
                            int flippedJ = DimY - 1 - j;

                            int index = (k * DimY * DimX) + (flippedJ * DimX) + flippedI;
                            pData[index] = (short)Math.Round(img.VoxelToDisplayValue(slice[i, j]));
                        }
                    }

                    Console.WriteLine($"Slice {k}/{DimZ} done.");
                }
            }
        }
        */


        //Get the houndsfield conversion

        double b = img.VoxelToDisplayValue(0);

        double a = img.VoxelToDisplayValue(1) - b;

        // Assuming: short[] data; int DimX, DimY, DimZ; and slice is allocated appropriately


        for (int k = 0; k < DimZ; k++)
        {
            img.GetVoxels(k, slice);

            for (int i = 0; i < DimX; i++)
            {
                for (int j = 0; j < DimY; j++)
                {
                    int flippedI = DimX - 1 - i;
                    int flippedJ = DimY - 1 - j;

                    int index = (k * DimY * DimX) + (flippedJ * DimX) + flippedI;
                    data[index] = (short)Math.Round(a * slice[i, j] + b);
                }
            }

            Console.WriteLine($"Slice {k}/{DimZ} done.");
        }

        Console.WriteLine("Nifti done.");
        return To3D(data);
    }
    private short[,,] To3D(short[] flat)
    {
        short[,,] result = new short[DimX, DimY, DimZ];

        int idx = 0;
        for (int z = 0; z < DimZ; z++)
        {
            for (int y = 0; y < DimY; y++)
            {
                for (int x = 0; x < DimX; x++)
                {
                    result[x, y, z] = flat[idx++];
                }
            }
        }

        return result;
    }

 
}