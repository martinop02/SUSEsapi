using System;
using System.Collections.Generic;
using System.Windows.Media;


public static class ColorCodes
{
    public static Dictionary<string, Color> GetDictionary()
    {
        var dict = new Dictionary<string, Color>
        {
            { "adrenal_gland_left", Colors.OrangeRed },
            { "adrenal_gland_right", Colors.OrangeRed },
            { "aorta", Colors.Red },
            { "atrial_appendage_left", Colors.DarkRed },
            { "autochthon_left", Colors.SaddleBrown },
            { "autochthon_right", Colors.SaddleBrown },
            { "brachiocephalic_trunk", Colors.Firebrick },
            { "brachiocephalic_vein_left", Colors.MediumBlue },
            { "brachiocephalic_vein_right", Colors.MediumBlue },
            { "brain", Color.FromRgb(107,115,0) },
            { "clavicula_left", Colors.Bisque },
            { "clavicula_right", Colors.Bisque },
            { "colon", Colors.Chocolate },
            { "common_carotid_artery_left", Colors.Red },
            { "common_carotid_artery_right", Colors.Red },
            { "costal_cartilages", Color.FromRgb(128,128,0) },
            { "duodenum", Colors.Peru },
            { "esophagus", Color.FromRgb(99,33,00) },
            { "femur_left", Color.FromRgb(10,102,69) },
            { "femur_right", Color.FromRgb(0,0,139) },
            { "gallbladder", Colors.Green },
            { "gluteus_maximus_left", Colors.Orange },
            { "gluteus_maximus_right", Colors.Orange },
            { "gluteus_medius_left", Colors.DarkOrange },
            { "gluteus_medius_right", Colors.DarkOrange },
            { "gluteus_minimus_left", Colors.Goldenrod },
            { "gluteus_minimus_right", Colors.Goldenrod },
            { "heart", Colors.Crimson },
            { "hip_left", Colors.Wheat },
            { "hip_right", Colors.Wheat },
            { "humerus_left", Color.FromRgb(10,102,69) },
            { "humerus_right", Color.FromRgb(0,0,139) },
            { "iliac_artery_left", Colors.Red },
            { "iliac_artery_right", Colors.Red },
            { "iliac_vena_left", Colors.Blue },
            { "iliac_vena_right", Colors.Blue },
            { "iliopsoas_left", Colors.SandyBrown },
            { "iliopsoas_right", Colors.SandyBrown },
            { "inferior_vena_cava", Color.FromRgb(159,207,255) },
            { "kidney_cyst_left", Colors.LightSeaGreen },
            { "kidney_cyst_right", Colors.LightSeaGreen },
            { "kidney_left", Color.FromRgb(255,198,203) },
            { "kidney_right", Colors.LightGreen },
            { "liver", Color.FromRgb(99,33,00) },
            { "lung_lower_lobe_left", Colors.DarkBlue },
            { "lung_lower_lobe_right", Colors.DarkBlue },
            { "lung_middle_lobe_right", Colors.DarkBlue },
            { "lung_upper_lobe_left", Colors.DarkBlue },
            { "lung_upper_lobe_right", Colors.DarkBlue },
            { "lung_left", Colors.DarkBlue },
            { "lung_right", Colors.DarkBlue },
            { "pancreas", Colors.PaleGoldenrod },
            { "portal_vein_and_splenic_vein", Colors.Blue },
            { "prostate", Colors.MediumSeaGreen },
            { "pulmonary_vein", Colors.DodgerBlue },

            // Ribs (gray for all)
            { "rib_left_1", Color.FromRgb(0,255,0)}, { "rib_left_2", Color.FromRgb(0,255,0) }, { "rib_left_3", Color.FromRgb(0,255,0) },
            { "rib_left_4", Color.FromRgb(0,255,0) }, { "rib_left_5", Color.FromRgb(0,255,0) }, { "rib_left_6", Color.FromRgb(0,255,0)},
            { "rib_left_7", Color.FromRgb(0,255,0) }, { "rib_left_8", Color.FromRgb(0,255,0) }, { "rib_left_9", Color.FromRgb(0,255,0) },
            { "rib_left_10", Color.FromRgb(0, 255, 0) }, { "rib_left_11", Color.FromRgb(0, 255, 0) }, { "rib_left_12", Color.FromRgb(0,255,0) },

            { "rib_right_1", Color.FromRgb(0, 255, 0) }, { "rib_right_2", Color.FromRgb(0, 255, 0) }, { "rib_right_3", Color.FromRgb(0, 255, 0) },
            { "rib_right_4", Color.FromRgb(0, 255, 0) }, { "rib_right_5", Color.FromRgb(0, 255, 0) }, { "rib_right_6", Color.FromRgb(0, 255, 0) },
            { "rib_right_7", Color.FromRgb(0, 255, 0) }, { "rib_right_8", Color.FromRgb(0, 255, 0) }, { "rib_right_9", Color.FromRgb(0, 255, 0) },
            { "rib_right_10", Color.FromRgb(0, 255, 0) }, { "rib_right_11", Color.FromRgb(0, 255, 0) }, { "rib_right_12", Color.FromRgb(0, 255, 0) }, {"ribs", Color.FromRgb(0, 255, 0) },
            
            // Bones
            { "sacrum", Color.FromRgb(0,255,0) },
            { "scapula_left", Color.FromRgb(0,255,0) },
            { "scapula_right", Color.FromRgb(0,255,0) },
            { "skull", Color.FromRgb(0,255,0) },
            { "sternum", Color.FromRgb(128,128,0) },

            // Organs
            { "small_bowel", Colors.BurlyWood },
            { "spinal_cord", Colors.Cyan },
            { "spleen", Colors.Blue },
            { "stomach", Colors.IndianRed },
            { "subclavian_artery_left", Colors.Red },
            { "subclavian_artery_right", Colors.Red },
            { "superior_vena_cava", Color.FromRgb(159,207,255) },
            { "thyroid_gland", Colors.Yellow },
            { "trachea", Color.FromRgb(87,87,255) },
            { "urinary_bladder", Colors.Yellow },

            // Vertebrae (shades of gray by region)
            { "vertebrae_C1", Color.FromRgb(0,128,0) }, { "vertebrae_C2", Color.FromRgb(0,128,0) }, { "vertebrae_C3", Color.FromRgb(0,128,0) },
            { "vertebrae_C4", Color.FromRgb(0,128,0) }, { "vertebrae_C5", Color.FromRgb(0,128,0) }, { "vertebrae_C6", Color.FromRgb(0,128,0) },
            { "vertebrae_C7", Color.FromRgb(0,128,0) },

            { "vertebrae_T1", Color.FromRgb(0,128,0) }, { "vertebrae_T2", Color.FromRgb(0,128,0) }, { "vertebrae_T3", Color.FromRgb(0, 128, 0) },
            { "vertebrae_T4", Color.FromRgb(0, 128, 0) }, { "vertebrae_T5", Color.FromRgb(0, 128, 0) }, { "vertebrae_T6", Color.FromRgb(0, 128, 0) },
            { "vertebrae_T7", Color.FromRgb(0, 128, 0) }, { "vertebrae_T8", Color.FromRgb(0, 128, 0) }, { "vertebrae_T9", Color.FromRgb(0, 128, 0) },
            { "vertebrae_T10", Color.FromRgb(0, 128, 0) }, { "vertebrae_T11", Color.FromRgb(0, 128, 0) }, { "vertebrae_T12", Color.FromRgb(0, 128, 0) },

            { "vertebrae_L1", Color.FromRgb(0, 128, 0) }, { "vertebrae_L2", Color.FromRgb(0, 128, 0) }, { "vertebrae_L3", Color.FromRgb(0, 128, 0) },
            { "vertebrae_L4", Color.FromRgb(0, 128, 0) }, { "vertebrae_L5", Color.FromRgb(0, 128, 0) },

            { "vertebrae_S1", Color.FromRgb(0, 128, 0) },

            {"vertebrae", Color.FromRgb(0, 128, 0) }
        };


        return dict;
    }
}
