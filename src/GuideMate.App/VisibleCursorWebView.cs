using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Wpf;

namespace GuideMate.App;

internal sealed class VisibleCursorWebView : WebView2CompositionControl
{
    // WPF exposes no public handle getter; this is covered against the bundled runtime.
    private static readonly PropertyInfo CursorHandle = typeof(Cursor).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic)!;

    static VisibleCursorWebView()
    {
        // The composition controller writes page cursors here, including the hidden cursor.
        CursorProperty.OverrideMetadata(typeof(VisibleCursorWebView), new FrameworkPropertyMetadata(
            Cursors.Arrow, FrameworkPropertyMetadataOptions.None, null,
            (_, value) => value is Cursor cursor
                && CursorHandle.GetValue(cursor) is SafeHandle { IsInvalid: false, IsClosed: false }
                    ? cursor : Cursors.Arrow));
    }
}
