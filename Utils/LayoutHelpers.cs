namespace SCLOCUA.Utils
{
    // Compatibility shim for legacy static calls.
    public static class LayoutHelpers
    {
        public static void AnchorIfExists(System.Windows.Forms.Control? c,
                                          System.Windows.Forms.AnchorStyles s) => c?.Anchor = s;
    }
}
