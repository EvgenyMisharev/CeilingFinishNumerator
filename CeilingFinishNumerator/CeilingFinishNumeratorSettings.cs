using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml.Serialization;

namespace CeilingFinishNumerator
{
    public class CeilingFinishNumeratorSettings
    {
        public string CeilingFinishNumberingSelectedName { get; set; }
        public bool ProcessSelectedLevel { get; set; }
        public bool SeparatedBySections { get; set; }
        public string SelectedLevelName { get; set; }
        public string SelectedParameterName { get; set; }
        public bool FillRoomBookParameters { get; set; }

        public static CeilingFinishNumeratorSettings GetSettings()
        {
            CeilingFinishNumeratorSettings settings = null;
            string assemblyPathAll = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string fileName = "CeilingFinishNumeratorSettings.xml";
            string assemblyPath = assemblyPathAll.Replace("CeilingFinishNumerator.dll", fileName);

            if (File.Exists(assemblyPath))
            {
                using (FileStream fs = new FileStream(assemblyPath, FileMode.Open))
                {
                    XmlSerializer xSer = new XmlSerializer(typeof(CeilingFinishNumeratorSettings));
                    settings = xSer.Deserialize(fs) as CeilingFinishNumeratorSettings;
                    fs.Close();
                }
            }
            else
            {
                settings = new CeilingFinishNumeratorSettings();
            }

            return settings;
        }

        public void SaveSettings()
        {
            string assemblyPathAll = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string fileName = "CeilingFinishNumeratorSettings.xml";
            string assemblyPath = assemblyPathAll.Replace("CeilingFinishNumerator.dll", fileName);

            if (File.Exists(assemblyPath))
            {
                File.Delete(assemblyPath);
            }

            using (FileStream fs = new FileStream(assemblyPath, FileMode.Create))
            {
                XmlSerializer xSer = new XmlSerializer(typeof(CeilingFinishNumeratorSettings));
                xSer.Serialize(fs, this);
                fs.Close();
            }
        }
    }
}

