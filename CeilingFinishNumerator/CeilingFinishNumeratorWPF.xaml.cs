using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CeilingFinishNumerator
{
    public partial class CeilingFinishNumeratorWPF : Window
    {
        public string CeilingFinishNumberingSelectedName;
        public bool ProcessSelectedLevel;
        public bool SeparatedBySections;
        public bool FillRoomBookParameters;

        private List<Level> Levels;
        public Level SelectedLevel = null;

        private List<Parameter> StringParameters;
        public Parameter SelectedParameter = null;

        private CeilingFinishNumeratorSettings CeilingFinishNumeratorSettingsItem;

        public CeilingFinishNumeratorWPF(List<Parameter> stringParameters, List<Level> levels)
        {
            Levels = levels;
            StringParameters = stringParameters;
            CeilingFinishNumeratorSettingsItem = CeilingFinishNumeratorSettings.GetSettings();

            InitializeComponent();

            comboBox_LevelSelection.ItemsSource = Levels;
            comboBox_LevelSelection.DisplayMemberPath = "Name";

            comboBox_RoomParameters.ItemsSource = StringParameters;
            comboBox_RoomParameters.DisplayMemberPath = "Definition.Name";

            // Загрузка сохраненных настроек
            if (CeilingFinishNumeratorSettingsItem != null)
            {
                if (CeilingFinishNumeratorSettingsItem.CeilingFinishNumberingSelectedName == "rbt_EndToEndThroughoutTheProject")
                {
                    rbt_EndToEndThroughoutTheProject.IsChecked = true;
                }
                else if (CeilingFinishNumeratorSettingsItem.CeilingFinishNumberingSelectedName == "rbt_SeparatedByLevels")
                {
                    rbt_SeparatedByLevels.IsChecked = true;
                }

                checkBox_ProcessSelectedLevel.IsChecked = CeilingFinishNumeratorSettingsItem.ProcessSelectedLevel;
                checkBox_SeparatedBySections.IsChecked = CeilingFinishNumeratorSettingsItem.SeparatedBySections;
                checkBox_FillRoomBookParameters.IsChecked = CeilingFinishNumeratorSettingsItem.FillRoomBookParameters;

                if (!string.IsNullOrEmpty(CeilingFinishNumeratorSettingsItem.SelectedLevelName))
                {
                    var selectedLevel = Levels.FirstOrDefault(l => l.Name == CeilingFinishNumeratorSettingsItem.SelectedLevelName);
                    if (selectedLevel != null)
                    {
                        comboBox_LevelSelection.SelectedItem = selectedLevel;
                    }
                    else
                    {
                        if (Levels.Any())
                        {
                            comboBox_LevelSelection.SelectedItem = Levels.First();
                        }
                    }
                }
                else
                {
                    if (Levels.Any())
                    {
                        comboBox_LevelSelection.SelectedItem = Levels.First();
                    }
                }

                if (!string.IsNullOrEmpty(CeilingFinishNumeratorSettingsItem.SelectedParameterName))
                {
                    var selectedParam = StringParameters.FirstOrDefault(p => p.Definition.Name == CeilingFinishNumeratorSettingsItem.SelectedParameterName);
                    if (selectedParam != null)
                    {
                        comboBox_RoomParameters.SelectedItem = selectedParam;
                    }
                    else
                    {
                        // Если параметр не найден, выбираем первый элемент списка, если список не пустой
                        if (StringParameters.Any())
                        {
                            comboBox_RoomParameters.SelectedItem = StringParameters.First();
                        }
                    }
                }
                else
                {
                    // Если SelectedParameterName не задан, выбираем первый элемент списка, если список не пустой
                    if (StringParameters.Any())
                    {
                        comboBox_RoomParameters.SelectedItem = StringParameters.First();
                    }
                }
            }
            else
            {
                if (Levels.Any())
                {
                    comboBox_LevelSelection.SelectedItem = Levels.First();
                }

                if (StringParameters.Any())
                {
                    comboBox_RoomParameters.SelectedItem = StringParameters.First();
                }
            }

            comboBox_RoomParameters.IsEnabled = checkBox_SeparatedBySections.IsChecked == true;

            UpdateControlsState();
        }

        private void btn_Ok_Click(object sender, RoutedEventArgs e)
        {
            if (rbt_EndToEndThroughoutTheProject.IsChecked == true)
            {
                CeilingFinishNumberingSelectedName = rbt_EndToEndThroughoutTheProject.Name;
            }
            else if (rbt_SeparatedByLevels.IsChecked == true)
            {
                CeilingFinishNumberingSelectedName = rbt_SeparatedByLevels.Name;
            }

            ProcessSelectedLevel = checkBox_ProcessSelectedLevel.IsChecked == true;
            SeparatedBySections = checkBox_SeparatedBySections.IsChecked == true;
            FillRoomBookParameters = checkBox_FillRoomBookParameters.IsChecked == true;

            SelectedLevel = comboBox_LevelSelection.SelectedItem as Level;
            SelectedParameter = comboBox_RoomParameters.SelectedItem as Parameter;

            SaveSettings();

            DialogResult = true;
            Close();
        }

        private void btn_Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CeilingFinishNumeratorWPF_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                btn_Ok_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                btn_Cancel_Click(sender, e);
            }
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            UpdateControlsState();
        }

        private void CheckBox_ProcessSelectedLevel_Checked(object sender, RoutedEventArgs e)
        {
            UpdateControlsState();
        }

        private void UpdateControlsState()
        {
            if (rbt_SeparatedByLevels != null)
            {
                bool isSeparatedByLevels = rbt_SeparatedByLevels.IsChecked == true;
                bool isProcessSelectedLevel = checkBox_ProcessSelectedLevel.IsChecked == true;

                comboBox_LevelSelection.IsEnabled = isSeparatedByLevels && isProcessSelectedLevel;
            }
        }

        private void CheckBox_StateChanged(object sender, RoutedEventArgs e)
        {
            comboBox_RoomParameters.IsEnabled = checkBox_SeparatedBySections.IsChecked == true;
        }

        private void SaveSettings()
        {
            CeilingFinishNumeratorSettingsItem = new CeilingFinishNumeratorSettings
            {
                CeilingFinishNumberingSelectedName = CeilingFinishNumberingSelectedName,
                FillRoomBookParameters = FillRoomBookParameters,
                SeparatedBySections = SeparatedBySections,
                SelectedParameterName = SelectedParameter?.Definition.Name,
                ProcessSelectedLevel = ProcessSelectedLevel,
                SelectedLevelName = SelectedLevel?.Name
            };

            CeilingFinishNumeratorSettingsItem.SaveSettings();
        }
    }
}
