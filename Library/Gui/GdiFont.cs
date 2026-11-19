  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/

using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OmenMon.External;

namespace OmenMon.Library {

    // Enables the loading of custom TrueType fonts from resources
    public static class GdiFont {

        private const Single DEFAULT_FONT_SIZE = 18;

        private static uint FontCount;
        private static PrivateFontCollection Data { get; set; }

        // Initializes the class
        static GdiFont() {

            // Set up the font collection
            if(Data == null)
                Data = new PrivateFontCollection();

        }

#region Font Loading
        // Loads a font
        public static void Add(byte[] resource) {
            IntPtr pointer;

            // Keep track of already loaded fonts
            uint fontCount = FontCount;

            // Allocate the memory and load the resource
            pointer = Marshal.AllocCoTaskMem(resource.Length);
            Marshal.Copy(resource, 0, pointer, resource.Length);

            // Call an external GDI routine to add the font
            Gdi32.AddFontMemResourceEx(pointer, (uint) resource.Length, IntPtr.Zero, ref fontCount);

            // Add the font to the collection
            Data.AddMemoryFont(pointer, resource.Length);

            // Increment the number of loaded fonts
            FontCount += fontCount;

            // Release the allocated memory
            Marshal.FreeCoTaskMem(pointer);
            pointer = IntPtr.Zero;

        }
#endregion

#region Font Retrieval
        // Retrieves a font family for use
        public static FontFamily Get(int index) {

            return Data.Families[index];

        }

        // Retrieves a font family by name.
        //
        // Prefer this over the index overload. PrivateFontCollection.Families is ordered
        // by the collection, not by the order fonts were added, so every Get(0) in the
        // program silently changes meaning the moment a second font is embedded — which
        // is what adding Inter alongside the IoMon figure font would otherwise have done.
        // Falls back rather than throwing: a missing font resource should degrade the
        // typography, not take the window down during construction.
        public static FontFamily Get(string name) {

            foreach(FontFamily family in Data.Families)
                if(family.Name == name)
                    return family;

            return Data.Families.Length > 0 ? Data.Families[0] : FontFamily.GenericSansSerif;

        }

        // Retrieves a font by family name, in points unless told otherwise
        public static Font Get(
            string name,
            Single size,
            FontStyle style = FontStyle.Regular,
            GraphicsUnit unit = GraphicsUnit.Point) {

            return new Font(Get(name), size, style, unit);

        }

        // Retrieves a font for use
        public static Font Get(
            int index,
            Single size = DEFAULT_FONT_SIZE,
            FontStyle style = FontStyle.Regular,
            GraphicsUnit unit = GraphicsUnit.Pixel) {

            // Create a new font object directly
            return new Font(Data.Families[index], size, style, unit);

        }
#endregion

    }

}
