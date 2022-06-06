using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CeilingFinishNumerator
{
    public partial class CeilingFinishNumeratorWPF : Window
    {
        public string CeilingFinishNumberingSelectedName;
        public CeilingFinishNumeratorWPF()
        {
            InitializeComponent();
        }

        private void btn_Ok_Click(object sender, RoutedEventArgs e)
        {
            CeilingFinishNumberingSelectedName = (groupBox_CeilingFinishNumbering.Content as System.Windows.Controls.Grid)
                .Children.OfType<RadioButton>()
                .FirstOrDefault(rb => rb.IsChecked.Value == true)
                .Name;
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
                CeilingFinishNumberingSelectedName = (groupBox_CeilingFinishNumbering.Content as System.Windows.Controls.Grid)
                    .Children.OfType<RadioButton>()
                    .FirstOrDefault(rb => rb.IsChecked.Value == true)
                    .Name;
                DialogResult = true;
                Close();
            }

            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }
    }
}
