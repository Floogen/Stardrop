using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stardrop.Models
{
    public class Mods : ObservableCollection<Mod>
    {
        public Mods(string modsFilePath)
        {
            Add(new Mod("TEST.1"));
            Add(new Mod("TEST.2"));
        }
    }
}
