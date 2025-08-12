using System.Windows.Forms;

namespace SCLOCUA.Extensions
{
    public static class ControlExtensions
    {
        public static void AnchorIfExists(this Control? control, AnchorStyles anchor)
        {
            if (control != null)
            {
                control.Anchor = anchor;
            }
        }
    }
}
