using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PopStudio.Language.Languages;

namespace PopStudio.Avalonia.Drop
{
    /// <summary>
    /// Path TextBox drop + extension gate + accept feedback animation.
    /// </summary>
    public static class DropAccept
    {
        static readonly IBrush AcceptBrush = SolidColorBrush.Parse("#2E7D32");
        static readonly Thickness AcceptThickness = new Thickness(2);
        static readonly ConditionalWeakTable<TextBox, DropVisualState> Visuals = new ConditionalWeakTable<TextBox, DropVisualState>();

        public static void AttachPathBox(TextBox box, Action<string> onPath, Func<PathRule> getRule)
        {
            AttachPathDrop(box, onPath, getRule);
            PathHint.Attach(box, getRule);
        }

        public static void AttachPathDrop(TextBox box, Action<string> onPath, Func<PathRule> getRule = null)
        {
            if (box == null || onPath == null)
            {
                return;
            }

            EnsureDropVisual(box);
            DragDrop.SetAllowDrop(box, true);

            box.AddHandler(DragDrop.DragOverEvent, (s, e) =>
            {
                string path = GetFirstLocalPath(e.Data);
                string reject = GetRejectReason(path, getRule);
                bool ok = reject == null && !string.IsNullOrEmpty(path);
                e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
                SetDropVisual(box, ok);
                if (ok)
                {
                    PathHint.ClearDropReject(box);
                }
                else if (!string.IsNullOrEmpty(reject))
                {
                    PathHint.ShowDropReject(box, reject);
                }
                e.Handled = true;
            });

            box.AddHandler(DragDrop.DragLeaveEvent, (s, e) =>
            {
                SetDropVisual(box, false);
                PathHint.KeepDropRejectBriefly(box);
            });

            box.AddHandler(DragDrop.DropEvent, (s, e) =>
            {
                SetDropVisual(box, false);
                string path = GetFirstLocalPath(e.Data);
                string reject = GetRejectReason(path, getRule);
                if (!string.IsNullOrEmpty(reject))
                {
                    PathHint.FlashDropReject(box, reject);
                    e.DragEffects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                PathHint.ClearDropReject(box);
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                onPath(path);
                e.Handled = true;
            });
        }

        public static void AttachFileContentDrop(TextBox box, Action<string> onText, params string[] extensions)
        {
            if (box == null || onText == null)
            {
                return;
            }

            string[] normalized = PathRule.Normalize(extensions) ?? new[] { ".lua", ".txt" };
            EnsureDropVisual(box);
            DragDrop.SetAllowDrop(box, true);

            box.AddHandler(DragDrop.DragOverEvent, (s, e) =>
            {
                string path = GetFirstLocalPath(e.Data);
                string reject = GetContentRejectReason(path, normalized);
                bool ok = reject == null;
                e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
                SetDropVisual(box, ok);
                if (ok)
                {
                    PathHint.ClearDropReject(box);
                }
                else
                {
                    PathHint.ShowDropReject(box, reject);
                }
                e.Handled = true;
            });

            box.AddHandler(DragDrop.DragLeaveEvent, (s, e) =>
            {
                SetDropVisual(box, false);
                PathHint.KeepDropRejectBriefly(box);
            });

            box.AddHandler(DragDrop.DropEvent, (s, e) =>
            {
                SetDropVisual(box, false);
                string path = GetFirstLocalPath(e.Data);
                string reject = GetContentRejectReason(path, normalized);
                if (reject != null)
                {
                    PathHint.FlashDropReject(box, reject);
                    e.Handled = true;
                    return;
                }

                PathHint.ClearDropReject(box);
                try
                {
                    onText(File.ReadAllText(path));
                    e.Handled = true;
                }
                catch
                {
                    PathHint.FlashDropReject(box, MAUIStr.Obj.Share_DropRejected);
                    e.Handled = true;
                }
            });
        }

        /// <summary>
        /// Accept several files/folders in one drop (multi-select path fields, e.g. the image page).
        /// Shares path extraction with the single-path helpers and applies no extension gate —
        /// the caller decides what to do with the batch. A <see cref="TextBox"/> also gets the
        /// accept feedback visual and keeps the caret at the end of the text.
        /// </summary>
        public static void AttachPathMulti(Control control, Action<IReadOnlyList<string>> onPaths)
        {
            if (control == null || onPaths == null)
            {
                return;
            }

            TextBox box = control as TextBox;
            if (box != null)
            {
                EnsureDropVisual(box);
            }

            DragDrop.SetAllowDrop(control, true);

            control.AddHandler(DragDrop.DragOverEvent, (s, e) =>
            {
                bool ok = GetLocalPaths(control, e.Data) != null;
                e.DragEffects = ok ? e.DragEffects & DragDropEffects.Copy : DragDropEffects.None;
                if (box != null)
                {
                    SetDropVisual(box, ok);
                }
                e.Handled = true;
            });

            control.AddHandler(DragDrop.DragLeaveEvent, (s, e) =>
            {
                if (box != null)
                {
                    SetDropVisual(box, false);
                }
            });

            control.AddHandler(DragDrop.DropEvent, (s, e) =>
            {
                if (box != null)
                {
                    SetDropVisual(box, false);
                }

                IReadOnlyList<string> paths = GetLocalPaths(control, e.Data);
                e.DragEffects = paths != null ? e.DragEffects & DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
                if (paths == null)
                {
                    return;
                }

                onPaths(paths);
                if (box != null)
                {
                    box.Focus();
                    box.CaretIndex = box.Text == null ? 0 : box.Text.Length;
                }
            });
        }

        /// <summary>
        /// Swallow drops without taking the path. Used for read-only display boxes so a drop on them
        /// does not bubble up to an outer drop target (the image page accepts drops on the whole page).
        /// </summary>
        public static void AttachDropSink(Control control)
        {
            if (control == null)
            {
                return;
            }

            DragDrop.SetAllowDrop(control, true);
            control.AddHandler(DragDrop.DragOverEvent, (s, e) =>
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
            });
            control.AddHandler(DragDrop.DropEvent, (s, e) =>
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
            });
        }

        static IReadOnlyList<string> GetLocalPaths(Control control, IDataObject data)
        {
            if (!control.IsEffectivelyEnabled)
            {
                return null;
            }
            if (control is TextBox textBox && textBox.IsReadOnly)
            {
                return null;
            }
            if (data == null || !data.Contains(DataFormats.Files))
            {
                return null;
            }

            List<string> paths = new List<string>();
            foreach (IStorageItem item in data.GetFiles() ?? Enumerable.Empty<IStorageItem>())
            {
                string path = item.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            return paths.Count == 0 ? null : paths;
        }

        static string GetRejectReason(string path, Func<PathRule> getRule)
        {
            if (string.IsNullOrEmpty(path))
            {
                return MAUIStr.Obj.Share_DropRejected;
            }

            PathRule rule = getRule?.Invoke();
            if (rule == null)
            {
                return null;
            }

            return rule.GetRejectReason(path);
        }

        static string GetContentRejectReason(string path, string[] normalized)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return MAUIStr.Obj.Share_DropNeedFile;
            }

            if (!PathRule.MatchesExtension(path, normalized))
            {
                return string.Format(MAUIStr.Obj.Share_ExtensionMismatch, string.Join(", ", normalized));
            }

            return null;
        }

        static DropVisualState EnsureDropVisual(TextBox box)
        {
            if (Visuals.TryGetValue(box, out DropVisualState state))
            {
                return state;
            }

            ScaleTransform scale = new ScaleTransform(1, 1)
            {
                Transitions = new Transitions
                {
                    new DoubleTransition
                    {
                        Property = ScaleTransform.ScaleXProperty,
                        Duration = TimeSpan.FromMilliseconds(160),
                        Easing = new CubicEaseOut()
                    },
                    new DoubleTransition
                    {
                        Property = ScaleTransform.ScaleYProperty,
                        Duration = TimeSpan.FromMilliseconds(160),
                        Easing = new CubicEaseOut()
                    }
                }
            };
            box.RenderTransform = scale;
            box.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

            state = new DropVisualState
            {
                Scale = scale
            };
            Visuals.Add(box, state);
            return state;
        }

        static void SetDropVisual(TextBox box, bool accept)
        {
            DropVisualState state = EnsureDropVisual(box);
            if (accept)
            {
                box.SetValue(TextBox.BorderBrushProperty, AcceptBrush);
                box.SetValue(TextBox.BorderThicknessProperty, AcceptThickness);
                state.Scale.ScaleX = 1.02;
                state.Scale.ScaleY = 1.02;
            }
            else
            {
                // Clear local overrides so theme/style brushes come back (null assign turns white).
                box.ClearValue(TextBox.BorderBrushProperty);
                box.ClearValue(TextBox.BorderThicknessProperty);
                state.Scale.ScaleX = 1;
                state.Scale.ScaleY = 1;
            }
        }

        static string GetFirstLocalPath(IDataObject data)
        {
            if (data == null || !data.Contains(DataFormats.Files))
            {
                return null;
            }

            IStorageItem first = data.GetFiles()?.FirstOrDefault();
            return first?.TryGetLocalPath();
        }

        sealed class DropVisualState
        {
            public ScaleTransform Scale;
        }
    }
}
