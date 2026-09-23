// Source: https://github.com/bertt/3dtiles_downloader/blob/f0512581977194754bde4c5b35eda73716ba3e10/src/TilesetJson.cs
// Licensed under the MIT License retained beside this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace threedtiles_exporter
{
    public class TileSetJson
    {
        public Root root { get; set; }
        public double geometricError { get; set; }
        public Asset asset { get; set; }
    }

    public class Child : ICloneable
    {
        public List<Child> children { get; set; }
        public double[] transform { get; set; }
        public double geometricError { get; set; }
        public string refine { get; set; }
        public Boundingvolume boundingVolume { get; set; }
        public Content content { get; set; }
        public object Clone()
        {
            var c = (Child)MemberwiseClone();
            c.content = (Content)content.Clone();
            return c;
        }
    }

    public class Root : Child
    {
    }

    public class Boundingvolume
    {
        private double[] _box;
        public double[] box
        {
            get
            {
                return this._box;
            }
            set
            {
                _box = value.Select(d => Math.Round(d, 6)).ToArray();
            }
        }
    }

    public class Content : ICloneable
    {
        public string uri { get; set; }

        public object Clone()
        {
            return MemberwiseClone();
        }

    }

    public class Asset
    {
        public string generator { get; set; }

        public string version { get; set; }
    }
}
