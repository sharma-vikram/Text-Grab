﻿using Fasetto.Word;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Text_Grab.Controls;
using Text_Grab.Models;
using Text_Grab.Properties;
using Text_Grab.Utilities;
using Windows.Globalization;
using Windows.Media.Ocr;
using ZXing;
using ZXing.Windows.Compatibility;

namespace Text_Grab.Views;

/// <summary>
/// Interaction logic for PersistentWindow.xaml
/// </summary>
public partial class GrabFrame : Window
{
    private bool isDrawing = false;
    private OcrResult? ocrResultOfWindow;
    private ObservableCollection<WordBorder> wordBorders = new();
    private DispatcherTimer reSearchTimer = new();
    private DispatcherTimer reDrawTimer = new();
    private bool isSelecting;
    private Point clickedPoint;
    private Border selectBorder = new();

    private ImageSource? frameContentImageSource;

    private ImageSource? droppedImageSource;

    private bool isSpaceJoining = true;

    private ResultTable? AnalyedResultTable;

    public bool IsFromEditWindow { get; set; } = false;

    public bool IsWordEditMode { get; set; } = true;

    public bool IsFreezeMode { get; set; } = false;

    private bool IsDragOver = false;

    private bool wasAltHeld = false;

    private bool isLanguageBoxLoaded = false;

    public string FrameText { get; private set; } = string.Empty;

    public TextBox? DestinationTextBox { get; set; }

    public GrabFrame()
    {
        InitializeComponent();

        LoadOcrLanguages();

        SetRestoreState();

        WindowResizer resizer = new(this);
        reDrawTimer.Interval = new(0, 0, 0, 0, 500);
        reDrawTimer.Tick += ReDrawTimer_Tick;
        reDrawTimer.Start();

        reSearchTimer.Interval = new(0, 0, 0, 0, 300);
        reSearchTimer.Tick += ReSearchTimer_Tick;

        RoutedCommand newCmd = new();
        _ = newCmd.InputGestures.Add(new KeyGesture(Key.Escape));
        _ = CommandBindings.Add(new CommandBinding(newCmd, Escape_Keyed));
    }

    public void GrabFrame_Loaded(object sender, RoutedEventArgs e)
    {
        this.PreviewMouseWheel += HandlePreviewMouseWheel;
        this.PreviewKeyDown += Window_PreviewKeyDown;
        this.PreviewKeyUp += Window_PreviewKeyUp;

        CheckBottomRowButtonsVis();
    }

    public void GrabFrame_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Activated -= GrabFrameWindow_Activated;
        this.Closed -= Window_Closed;
        this.Deactivated -= GrabFrameWindow_Deactivated;
        this.DragLeave -= GrabFrameWindow_DragLeave;
        this.DragOver -= GrabFrameWindow_DragOver;
        this.Loaded -= GrabFrame_Loaded;
        this.LocationChanged -= Window_LocationChanged;
        this.SizeChanged -= Window_SizeChanged;
        this.Unloaded -= GrabFrame_Unloaded;
        this.PreviewMouseWheel -= HandlePreviewMouseWheel;
        this.PreviewKeyDown -= Window_PreviewKeyDown;
        this.PreviewKeyUp -= Window_PreviewKeyUp;

        reDrawTimer.Stop();
        reDrawTimer.Tick -= ReDrawTimer_Tick;

        MinimizeButton.Click -= OnMinimizeButtonClick;
        RestoreButton.Click -= OnRestoreButtonClick;
        CloseButton.Click -= OnCloseButtonClick;

        RectanglesCanvas.MouseDown -= RectanglesCanvas_MouseDown;
        RectanglesCanvas.MouseMove -= RectanglesCanvas_MouseMove;
        RectanglesCanvas.MouseUp -= RectanglesCanvas_MouseUp;

        AspectRationMI.Checked -= AspectRationMI_Checked;
        AspectRationMI.Unchecked -= AspectRationMI_Checked;
        FreezeMI.Click -= FreezeMI_Click;

        SearchBox.GotFocus -= SearchBox_GotFocus;
        SearchBox.TextChanged -= SearchBox_TextChanged;

        ClearBTN.Click -= ClearBTN_Click;
        ExactMatchChkBx.Click -= ExactMatchChkBx_Click;

        RefreshBTN.Click -= RefreshBTN_Click;
        FreezeToggleButton.Click -= FreezeToggleButton_Click;
        TableToggleButton.Click -= TableToggleButton_Click;
        EditToggleButton.Click -= EditToggleButton_Click;
        SettingsBTN.Click -= SettingsBTN_Click;
        EditTextBTN.Click -= EditTextBTN_Click;
        GrabBTN.Click -= GrabBTN_Click;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (wasAltHeld == true && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt))
        {
            RectanglesCanvas.Opacity = 1;
            wasAltHeld = false;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (wasAltHeld == false && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt))
        {
            RectanglesCanvas.Opacity = 0.1;
            wasAltHeld = true;
        }
    }

    private void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Source: StackOverflow, read on Sep. 10, 2021
        // https://stackoverflow.com/a/53698638/7438031

        if (this.WindowState == WindowState.Maximized)
            return;

        e.Handled = true;
        ResetGrabFrame();
        double aspectRatio = (this.Height - 66) / (this.Width - 4);

        bool isShiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        bool isCtrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

        if (e.Delta > 0)
        {
            this.Width += 100;
            this.Left -= 50;

            if (!isShiftDown)
            {
                this.Height += 100 * aspectRatio;
                this.Top -= 50 * aspectRatio;
            }
        }
        else if (e.Delta < 0)
        {
            if (this.Width > 120 && this.Height > 120)
            {
                this.Width -= 100;
                this.Left += 50;

                if (!isShiftDown)
                {
                    this.Height -= 100 * aspectRatio;
                    this.Top += 50 * aspectRatio;
                }
            }
        }
    }

    private void GrabFrameWindow_Initialized(object sender, EventArgs e)
    {
        WindowUtilities.SetWindowPosition(this);
        CheckBottomRowButtonsVis();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        string windowSizeAndPosition = $"{this.Left},{this.Top},{this.Width},{this.Height}";
        Properties.Settings.Default.GrabFrameWindowSizeAndPosition = windowSizeAndPosition;
        Properties.Settings.Default.Save();

        WindowUtilities.ShouldShutDown();
    }

    private void Escape_Keyed(object sender, ExecutedRoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text) == false && SearchBox.Text != "Search For Text...")
            SearchBox.Text = "";
        else if (RectanglesCanvas.Children.Count > 0)
            ResetGrabFrame();
        else
            Close();
    }

    private async void ReDrawTimer_Tick(object? sender, EventArgs? e)
    {
        reDrawTimer.Stop();
        ResetGrabFrame();

        frameContentImageSource = ImageMethods.GetWindowBoundsImage(this);
        if (SearchBox.Text is string searchText)
            await DrawRectanglesAroundWords(searchText);
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GrabBTN_Click(object sender, RoutedEventArgs e)
    {
        if (IsFromEditWindow == false
            && string.IsNullOrWhiteSpace(FrameText) == false
            && Settings.Default.NeverAutoUseClipboard == false)
            try { Clipboard.SetDataObject(FrameText, true); } catch { }

        if (Settings.Default.ShowToast == true
            && IsFromEditWindow == false)
            NotificationUtilities.ShowToast(FrameText);

        if (IsFromEditWindow == true
            && string.IsNullOrWhiteSpace(FrameText) == false
            && DestinationTextBox is not null)
        {
            DestinationTextBox.Select(DestinationTextBox.SelectionStart + DestinationTextBox.SelectionLength, 0);
            DestinationTextBox.AppendText(Environment.NewLine);
            UpdateFrameText();
        }
    }

    private void ResetGrabFrame()
    {
        ocrResultOfWindow = null;
        frameContentImageSource = null;
        RectanglesCanvas.Children.Clear();
        wordBorders.Clear();
        MatchesTXTBLK.Text = "Matches: 0";
        UpdateFrameText();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || IsFreezeMode || isMiddleDown)
            return;

        ResetGrabFrame();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        ResetGrabFrame();
        CheckBottomRowButtonsVis();
        SetRestoreState();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void CheckBottomRowButtonsVis()
    {
        if (this.Width < 300)
        {
            ButtonsStackPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ButtonsStackPanel.Visibility = Visibility.Visible;
        }

        if (this.Width < 460)
        {
            SearchBox.Visibility = Visibility.Collapsed;
            MatchesTXTBLK.Visibility = Visibility.Collapsed;
            ClearBTN.Visibility = Visibility.Collapsed;
        }
        else
        {
            SearchBox.Visibility = Visibility.Visible;
            ClearBTN.Visibility = Visibility.Visible;
        }

        if (this.Width < 580)
            LanguagesComboBox.Visibility = Visibility.Collapsed;
        else
            LanguagesComboBox.Visibility = Visibility.Visible;
    }

    private void GrabFrameWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!IsWordEditMode && !IsFreezeMode)
            ResetGrabFrame();
        else
        {
            RectanglesCanvas.Opacity = 1;
            if (Keyboard.Modifiers != ModifierKeys.Alt)
                wasAltHeld = false;

            if (!IsFreezeMode)
                FreezeGrabFrame();
        }

    }

    private async Task DrawRectanglesAroundWords(string searchWord = "")
    {
        if (isDrawing || IsDragOver)
            return;

        isDrawing = true;

        if (string.IsNullOrWhiteSpace(searchWord))
            searchWord = SearchBox.Text;

        RectanglesCanvas.Children.Clear();
        wordBorders.Clear();

        Point windowPosition = this.GetAbsolutePosition();
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        System.Drawing.Rectangle rectCanvasSize = new System.Drawing.Rectangle
        {
            Width = (int)((ActualWidth + 2) * dpi.DpiScaleX),
            Height = (int)((ActualHeight - 64) * dpi.DpiScaleY),
            X = (int)((windowPosition.X - 2) * dpi.DpiScaleX),
            Y = (int)((windowPosition.Y + 24) * dpi.DpiScaleY)
        };

        double scale = 1;
        Language? currentLang = LanguagesComboBox.SelectedItem as Language;

        if (ocrResultOfWindow == null || ocrResultOfWindow.Lines.Count == 0)
            (ocrResultOfWindow, scale) = await ImageMethods.GetOcrResultFromRegion(rectCanvasSize, currentLang);

        if (currentLang is not null)
        {
            if (currentLang.LanguageTag.StartsWith("zh", StringComparison.InvariantCultureIgnoreCase) == true)
                isSpaceJoining = false;
            else if (currentLang.LanguageTag.StartsWith("ja", StringComparison.InvariantCultureIgnoreCase) == true)
                isSpaceJoining = false;
        }

        if (ocrResultOfWindow == null)
            return;

        int numberOfMatches = 0;
        int lineNumber = 0;

        foreach (OcrLine ocrLine in ocrResultOfWindow.Lines)
        {
            double top = ocrLine.Words.Select(x => x.BoundingRect.Top).Min();
            double bottom = ocrLine.Words.Select(x => x.BoundingRect.Bottom).Max();
            double left = ocrLine.Words.Select(x => x.BoundingRect.Left).Min();
            double right = ocrLine.Words.Select(x => x.BoundingRect.Right).Max();

            StringBuilder lineText = new();

            ocrLine.GetTextFromOcrLine(isSpaceJoining, lineText);

            Rect lineRect = new()
            {
                X = left,
                Y = top,
                Width = Math.Abs(right - left),
                Height = Math.Abs(bottom - top)
            };

            WordBorder wordBorderBox = new()
            {
                Width = lineRect.Width / (dpi.DpiScaleX * scale),
                Height = lineRect.Height / (dpi.DpiScaleY * scale),
                Word = lineText.ToString().Trim(),
                ToolTip = ocrLine.Text,
                LineNumber = lineNumber,
                IsFromEditWindow = IsFromEditWindow
            };

            if ((bool)ExactMatchChkBx.IsChecked!)
            {
                if (lineText.ToString().Equals(searchWord, StringComparison.CurrentCulture))
                {
                    wordBorderBox.Select();
                    numberOfMatches++;
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(searchWord)
                    && lineText.ToString().Contains(searchWord, StringComparison.CurrentCultureIgnoreCase))
                {
                    wordBorderBox.Select();
                    numberOfMatches++;
                }
            }
            wordBorders.Add(wordBorderBox);
            _ = RectanglesCanvas.Children.Add(wordBorderBox);
            Canvas.SetLeft(wordBorderBox, lineRect.Left / (dpi.DpiScaleX * scale));
            Canvas.SetTop(wordBorderBox, lineRect.Top / (dpi.DpiScaleY * scale));

            lineNumber++;
        }

        if (ocrResultOfWindow != null && ocrResultOfWindow.TextAngle != null)
        {
            RotateTransform transform = new((double)ocrResultOfWindow.TextAngle)
            {
                CenterX = (Width - 4) / 2,
                CenterY = (Height - 60) / 2
            };
            RectanglesCanvas.RenderTransform = transform;
        }
        else
        {
            RotateTransform transform = new(0)
            {
                CenterX = (Width - 4) / 2,
                CenterY = (Height - 60) / 2
            };
            RectanglesCanvas.RenderTransform = transform;
        }

        if (TableToggleButton.IsChecked == true)
        {
            try
            {
                AnalyzeAsTable(rectCanvasSize);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        if (Settings.Default.TryToReadBarcodes)
            TryToReadBarcodes(dpi);

        List<WordBorder> wordBordersRePlace = new();
        foreach (UIElement child in RectanglesCanvas.Children)
        {
            if (child is WordBorder wordBorder)
                wordBordersRePlace.Add(wordBorder);
        }
        RectanglesCanvas.Children.Clear();
        foreach (WordBorder wordBorder in wordBordersRePlace)
        {
            // First the Word borders are placed smaller, then table analysis occurs.
            // After table can be analyzed with the position of the word borders they are adjusted 
            wordBorder.Width += 16;
            wordBorder.Height += 4;
            double leftWB = Canvas.GetLeft(wordBorder);
            double topWB = Canvas.GetTop(wordBorder);
            Canvas.SetLeft(wordBorder, leftWB - 10);
            Canvas.SetTop(wordBorder, topWB - 2);
            RectanglesCanvas.Children.Add(wordBorder);
        }


        if (TableToggleButton.IsChecked == true && AnalyedResultTable is not null)
        {
            DrawTable(AnalyedResultTable);
        }

        if (IsWordEditMode == true)
            EnterEditMode();

        MatchesTXTBLK.Text = $"Matches: {numberOfMatches}";
        isDrawing = false;

        UpdateFrameText();
    }

    private void TryToReadBarcodes(DpiScale dpi)
    {
        System.Drawing.Bitmap bitmapOfGrabFrame = ImageMethods.GetWindowsBoundsBitmap(this);

        BarcodeReader barcodeReader = new()
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions { TryHarder = true }
        };

        ZXing.Result result = barcodeReader.Decode(bitmapOfGrabFrame);

        if (result is not null)
        {
            ResultPoint[] rawPoints = result.ResultPoints;

            float[] xs = rawPoints.Reverse().Take(4).Select(x => x.X).ToArray();
            float[] ys = rawPoints.Reverse().Take(4).Select(x => x.Y).ToArray();

            Point minPoint = new Point(xs.Min(), ys.Min());
            Point maxPoint = new Point(xs.Max(), ys.Max());
            Point diffs = new Point(maxPoint.X - minPoint.X, maxPoint.Y - minPoint.Y);

            if (diffs.Y < 5)
                diffs.Y = diffs.X / 10;


            WordBorder wb = new();
            wb.Word = result.Text;
            wb.Width = diffs.X / dpi.DpiScaleX + 12;
            wb.Height = diffs.Y / dpi.DpiScaleY + 12;
            wb.SetAsBarcode();
            wordBorders.Add(wb);
            _ = RectanglesCanvas.Children.Add(wb);
            double left = minPoint.X / (dpi.DpiScaleX) - 6;
            double top = minPoint.Y / (dpi.DpiScaleY) - 6;
            Canvas.SetLeft(wb, left);
            Canvas.SetTop(wb, top);
        }
    }

    private void DrawTable(ResultTable resultTable)
    {
        // Draw the lines and bounds of the table
        SolidColorBrush tableColor = new SolidColorBrush(Color.FromArgb(255, 40, 118, 126));

        Border tableOutline = new()
        {
            Width = resultTable.BoundingRect.Width,
            Height = resultTable.BoundingRect.Height,
            BorderThickness = new Thickness(3),
            BorderBrush = tableColor
        };
        RectanglesCanvas.Children.Add(tableOutline);
        Canvas.SetTop(tableOutline, resultTable.BoundingRect.Y);
        Canvas.SetLeft(tableOutline, resultTable.BoundingRect.X);

        foreach (int columnLine in resultTable.ColumnLines)
        {
            Border vertLine = new()
            {
                Width = 2,
                Height = resultTable.BoundingRect.Height,
                Background = tableColor
            };
            RectanglesCanvas.Children.Add(vertLine);
            Canvas.SetTop(vertLine, resultTable.BoundingRect.Y);
            Canvas.SetLeft(vertLine, columnLine);
        }

        foreach (int rowLine in resultTable.RowLines)
        {
            Border horzLine = new()
            {
                Height = 2,
                Width = resultTable.BoundingRect.Width,
                Background = tableColor
            };
            RectanglesCanvas.Children.Add(horzLine);
            Canvas.SetTop(horzLine, rowLine);
            Canvas.SetLeft(horzLine, resultTable.BoundingRect.X);
        }
    }

    private void AnalyzeAsTable(System.Drawing.Rectangle rectCanvasSize)
    {
        int hitGridSpacing = 3;

        int numberOfVerticalLines = rectCanvasSize.Width / hitGridSpacing;
        int numberOfHorizontalLines = rectCanvasSize.Height / hitGridSpacing;

        List<ResultRow> resultRows = new();

        List<int> rowAreas = new();
        for (int i = 0; i < numberOfHorizontalLines; i++)
        {
            Border horzLine = new()
            {
                Height = 1,
                Width = rectCanvasSize.Width,
                Opacity = 0,
                Background = new SolidColorBrush(Colors.Gray)
            };
            Rect horzLineRect = new(0, i * hitGridSpacing, horzLine.Width, horzLine.Height);
            _ = RectanglesCanvas.Children.Add(horzLine);
            Canvas.SetTop(horzLine, i * 3);

            foreach (var child in RectanglesCanvas.Children)
            {
                if (child is WordBorder wb)
                {
                    Rect wbRect = new Rect(Canvas.GetLeft(wb), Canvas.GetTop(wb), wb.Width, wb.Height);
                    if (horzLineRect.IntersectsWith(wbRect) == true)
                    {
                        rowAreas.Add(i * hitGridSpacing);
                        break;
                    }
                }
            }
        }

        int rowTop = 0;
        int rowCount = 0;
        for (int i = 0; i < rowAreas.Count; i++)
        {
            int thisLine = rowAreas[i];

            // check if should set this as top
            if (i == 0)
                rowTop = thisLine;
            else if (i - 1 > 0)
            {
                int prevRow = rowAreas[i - 1];
                if (thisLine - prevRow != hitGridSpacing)
                {
                    rowTop = thisLine;
                }
            }

            // check to see if at bottom of row
            if (i == rowAreas.Count - 1)
            {
                resultRows.Add(new ResultRow { Top = rowTop, Bottom = thisLine, ID = rowCount });
                rowCount++;
            }
            else if (i + 1 < rowAreas.Count)
            {
                int nextRow = rowAreas[i + 1];
                if (nextRow - thisLine != hitGridSpacing)
                {
                    resultRows.Add(new ResultRow { Top = rowTop, Bottom = thisLine, ID = rowCount });
                    rowCount++;
                }
            }
        }

        List<int> columnAreas = new();
        for (int i = 0; i < numberOfVerticalLines; i++)
        {
            Border vertLine = new()
            {
                Height = rectCanvasSize.Height,
                Width = 1,
                Opacity = 0,
                Background = new SolidColorBrush(Colors.Gray)
            };
            _ = RectanglesCanvas.Children.Add(vertLine);
            Canvas.SetLeft(vertLine, i * hitGridSpacing);

            Rect vertLineRect = new(i * hitGridSpacing, 0, vertLine.Width, vertLine.Height);
            foreach (var child in RectanglesCanvas.Children)
            {
                if (child is WordBorder wb)
                {
                    Rect wbRect = new Rect(Canvas.GetLeft(wb), Canvas.GetTop(wb), wb.Width, wb.Height);
                    if (vertLineRect.IntersectsWith(wbRect) == true)
                    {
                        columnAreas.Add(i * hitGridSpacing);
                        break;
                    }
                }
            }
        }

        List<ResultColumn> resultColumns = new();
        int columnLeft = 0;
        int columnCount = 0;
        for (int i = 0; i < columnAreas.Count; i++)
        {
            int thisLine = columnAreas[i];

            // check if should set this as top
            if (i == 0)
                columnLeft = thisLine;
            else if (i - 1 > 0)
            {
                int prevColumn = columnAreas[i - 1];
                if (thisLine - prevColumn != hitGridSpacing)
                {
                    columnLeft = thisLine;
                }
            }

            // check to see if at last Column
            if (i == columnAreas.Count - 1)
            {
                resultColumns.Add(new ResultColumn { Left = columnLeft, Right = thisLine, ID = columnCount });
                columnCount++;
            }
            else if (i + 1 < columnAreas.Count)
            {
                int nextColumn = columnAreas[i + 1];
                if (nextColumn - thisLine != hitGridSpacing)
                {
                    resultColumns.Add(new ResultColumn { Left = columnLeft, Right = thisLine, ID = columnCount });
                    columnCount++;
                }
            }
        }

        Rect tableBoundingRect = new()
        {
            X = columnAreas.FirstOrDefault(),
            Y = rowAreas.FirstOrDefault(),
            Width = columnAreas.LastOrDefault() - columnAreas.FirstOrDefault(),
            Height = rowAreas.LastOrDefault() - rowAreas.FirstOrDefault()
        };

        // try 4 times to refine the rows and columns for outliers
        // on the fifth time set the word boundery properties
        for (int r = 0; r < 5; r++)
        {
            int outlierThreshould = 2;

            List<int> outlierRowIDs = new();

            foreach (ResultRow row in resultRows)
            {
                int numberOfIntersectingWords = 0;
                Border rowBorder = new()
                {
                    Height = row.Bottom - row.Top,
                    Width = tableBoundingRect.Width,
                    Background = new SolidColorBrush(Colors.Red),
                    Tag = row.ID
                };
                RectanglesCanvas.Children.Add(rowBorder);
                Canvas.SetLeft(rowBorder, tableBoundingRect.X);
                Canvas.SetTop(rowBorder, row.Top);

                Rect rowRect = new Rect(tableBoundingRect.X, row.Top, rowBorder.Width, rowBorder.Height);
                foreach (var child in RectanglesCanvas.Children)
                {
                    if (child is WordBorder wb)
                    {
                        Rect wbRect = new Rect(Canvas.GetLeft(wb), Canvas.GetTop(wb), wb.Width, wb.Height);
                        if (rowRect.IntersectsWith(wbRect) == true)
                        {
                            numberOfIntersectingWords++;
                            wb.ResultRowID = row.ID;
                        }
                    }
                }

                if (numberOfIntersectingWords <= outlierThreshould && r != 4)
                    outlierRowIDs.Add(row.ID);
            }

            if (outlierRowIDs.Count > 0)
                mergeTheseRowIDs(resultRows, outlierRowIDs);


            List<int> outlierColumnIDs = new();

            foreach (ResultColumn column in resultColumns)
            {
                int numberOfIntersectingWords = 0;
                Border columnBorder = new()
                {
                    Height = tableBoundingRect.Height,
                    Width = column.Right - column.Left,
                    Background = new SolidColorBrush(Colors.Blue),
                    Opacity = 0.2,
                    Tag = column.ID
                };
                RectanglesCanvas.Children.Add(columnBorder);
                Canvas.SetLeft(columnBorder, column.Left);
                Canvas.SetTop(columnBorder, tableBoundingRect.Y);

                Rect columnRect = new Rect(column.Left, tableBoundingRect.Y, columnBorder.Width, columnBorder.Height);
                foreach (var child in RectanglesCanvas.Children)
                {
                    if (child is WordBorder wb)
                    {
                        Rect wbRect = new Rect(Canvas.GetLeft(wb), Canvas.GetTop(wb), wb.Width, wb.Height);
                        if (columnRect.IntersectsWith(wbRect) == true)
                        {
                            numberOfIntersectingWords++;
                            wb.ResultColumnID = column.ID;
                        }
                    }
                }

                if (numberOfIntersectingWords <= outlierThreshould)
                    outlierColumnIDs.Add(column.ID);
            }

            if (outlierColumnIDs.Count > 0 && r != 4)
                mergetheseColumnIDs(resultColumns, outlierColumnIDs);
        }

        AnalyedResultTable = new ResultTable(resultColumns, resultRows);

        // foreach (ResultRow row in resultRows)
        // {
        //     Border rowBorder = new()
        //     {
        //         Height = row.Bottom - row.Top,
        //         Width = tableBoundingRect.Width,
        //         Background = new SolidColorBrush(Colors.Red),
        //         Opacity = 0.2,
        //         Tag = row.ID
        //     };
        //     RectanglesCanvas.Children.Add(rowBorder);
        //     Canvas.SetLeft(rowBorder, tableBoundingRect.X);
        //     Canvas.SetTop(rowBorder, row.Top);
        // }

        // foreach (ResultColumn column in resultColumns)
        // {
        //     Border columnBorder = new()
        //     {
        //         Height = tableBoundingRect.Height,
        //         Width = column.Right - column.Left,
        //         Background = new SolidColorBrush(Colors.Blue),
        //         Opacity = 0.2,
        //         Tag = column.ID
        //     };
        //     RectanglesCanvas.Children.Add(columnBorder);
        //     Canvas.SetLeft(columnBorder, column.Left);
        //     Canvas.SetTop(columnBorder, tableBoundingRect.Y);
        // }
    }

    private static void mergetheseColumnIDs(List<ResultColumn> resultColumns, List<int> outlierColumnIDs)
    {
        for (int i = 0; i < outlierColumnIDs.Count; i++)
        {
            for (int j = 0; j < resultColumns.Count; j++)
            {
                ResultColumn jthColumn = resultColumns[j];
                if (jthColumn.ID == outlierColumnIDs[i])
                {
                    if (j == 0)
                    {
                        // merge with next column if possible
                        if (j + 1 < resultColumns.Count)
                        {
                            ResultColumn nextColumn = resultColumns[j + 1];
                            nextColumn.Left = jthColumn.Left;
                        }
                    }
                    else if (j == resultColumns.Count - 1)
                    {
                        // merge with previous column
                        if (j - 1 >= 0)
                        {
                            ResultColumn prevColumn = resultColumns[j - 1];
                            prevColumn.Right = jthColumn.Right;
                        }
                    }
                    else
                    {
                        // merge with closet column
                        ResultColumn prevColumn = resultColumns[j - 1];
                        ResultColumn nextColumn = resultColumns[j + 1];
                        int distToPrev = (int)(jthColumn.Left - prevColumn.Right);
                        int distToNext = (int)(nextColumn.Left - jthColumn.Right);

                        if (distToNext < distToPrev)
                        {
                            // merge with next column
                            nextColumn.Left = jthColumn.Left;
                        }
                        else
                        {
                            // merge with prev column
                            prevColumn.Right = jthColumn.Right;
                        }
                    }
                    resultColumns.RemoveAt(j);
                }
            }
        }
    }

    private static void mergeTheseRowIDs(List<ResultRow> resultRows, List<int> outlierRowIDs)
    {

    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded == false)
            return;

        if (sender is not TextBox searchBox) return;

        reSearchTimer.Stop();
        reSearchTimer.Start();
    }

    private void ReSearchTimer_Tick(object? sender, EventArgs e)
    {
        reSearchTimer.Stop();
        if (SearchBox.Text is not string searchText)
            return;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            foreach (WordBorder wb in wordBorders)
                wb.Deselect();
        }
        else
        {
            foreach (WordBorder wb in wordBorders)
            {
                if (!string.IsNullOrWhiteSpace(searchText)
                    && wb.Word.ToLower().Contains(searchText.ToLower()))
                    wb.Select();
                else
                    wb.Deselect();
            }
        }

        UpdateFrameText();

        MatchesTXTBLK.Visibility = Visibility.Visible;
    }

    private void UpdateFrameText()
    {
        string[] selectedWbs = wordBorders.Where(w => w.IsSelected).Select(t => t.Word).ToArray();

        StringBuilder stringBuilder = new();

        if (TableToggleButton.IsChecked == true)
        {
            List<WordBorder>? selectedBorders = wordBorders.Where(w => w.IsSelected == true).ToList();

            if (selectedBorders.Count == 0)
                selectedBorders.AddRange(wordBorders);

            List<string> lineList = new();
            int? lastLineNum = 0;
            int lastColumnNum = 0;

            if (selectedBorders.FirstOrDefault() != null)
                lastLineNum = selectedBorders.FirstOrDefault()!.LineNumber;

            selectedBorders = selectedBorders.OrderBy(x => x.ResultColumnID).ToList();
            selectedBorders = selectedBorders.OrderBy(x => x.ResultRowID).ToList();

            int numberOfDistinctRows = selectedBorders.Select(x => x.ResultRowID).Distinct().Count();

            foreach (WordBorder border in selectedBorders)
            {
                if (lineList.Count == 0)
                    lastLineNum = border.ResultRowID;

                if (border.ResultRowID != lastLineNum)
                {
                    if (isSpaceJoining)
                        stringBuilder.Append(string.Join(' ', lineList));
                    else
                        stringBuilder.Append(string.Join("", lineList));
                    stringBuilder.Replace(" \t ", "\t");
                    stringBuilder.Append(Environment.NewLine);
                    lineList.Clear();
                    lastLineNum = border.ResultRowID;
                }

                if (border.ResultColumnID != lastColumnNum && numberOfDistinctRows > 1)
                {
                    string borderWord = border.Word;
                    int numberOfOffColumns = border.ResultColumnID - lastColumnNum;
                    if (numberOfOffColumns < 0)
                        lastColumnNum = 0;

                    numberOfOffColumns = border.ResultColumnID - lastColumnNum;

                    if (numberOfOffColumns > 0)
                        lineList.Add(new string('\t', numberOfOffColumns));
                }
                lastColumnNum = border.ResultColumnID;

                lineList.Add(border.Word);
            }

            if (isSpaceJoining)
                stringBuilder.Append(string.Join(' ', lineList));
            else
                stringBuilder.Append(string.Join("", lineList));
        }
        else
        {
            if (selectedWbs.Length > 0)
                stringBuilder.AppendJoin(Environment.NewLine, selectedWbs);
            else
                stringBuilder.AppendJoin(Environment.NewLine, wordBorders.Select(w => w.Word).ToArray());
        }

        FrameText = stringBuilder.ToString();

        if (IsFromEditWindow && DestinationTextBox is not null)
        {
            DestinationTextBox.SelectedText = FrameText;
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchBox is TextBox searchBox)
            searchBox.Text = "";
    }

    private void ExactMatchChkBx_Click(object sender, RoutedEventArgs e)
    {
        reSearchTimer.Stop();
        reSearchTimer.Start();
    }

    private void ClearBTN_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
    }

    private void RefreshBTN_Click(object sender, RoutedEventArgs e)
    {
        TextBox searchBox = SearchBox;
        ResetGrabFrame();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void SettingsBTN_Click(object sender, RoutedEventArgs e)
    {
        WindowUtilities.OpenOrActivateWindow<SettingsWindow>();
    }

    private void GrabFrameWindow_Activated(object? sender, EventArgs e)
    {
        RectanglesCanvas.Opacity = 1;
        if (!IsWordEditMode && !IsFreezeMode)
            reDrawTimer.Start();
        else
            UpdateFrameText();
    }

    private bool isMiddleDown = false;

    private void RectanglesCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed)
        {
            e.Handled = false;
            return;
        }

        isSelecting = true;
        clickedPoint = e.GetPosition(RectanglesCanvas);
        RectanglesCanvas.CaptureMouse();
        selectBorder.Height = 1;
        selectBorder.Width = 1;

        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            e.Handled = true;

            isMiddleDown = true;
            ResetGrabFrame();
            UnfreezeGrabFrame();
            return;
        }

        CursorClipper.ClipCursor(RectanglesCanvas);

        try { RectanglesCanvas.Children.Remove(selectBorder); } catch (Exception) { }

        selectBorder.BorderThickness = new Thickness(2);
        Color borderColor = Color.FromArgb(255, 40, 118, 126);
        selectBorder.BorderBrush = new SolidColorBrush(borderColor);
        Color backgroundColor = Color.FromArgb(15, 40, 118, 126);
        selectBorder.Background = new SolidColorBrush(backgroundColor);
        _ = RectanglesCanvas.Children.Add(selectBorder);
        Canvas.SetLeft(selectBorder, clickedPoint.X);
        Canvas.SetTop(selectBorder, clickedPoint.Y);
    }

    private async void RectanglesCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        isSelecting = false;
        CursorClipper.UnClipCursor();
        RectanglesCanvas.ReleaseMouseCapture();

        if (e.ChangedButton == MouseButton.Middle)
        {
            isMiddleDown = false;
            FreezeGrabFrame();

            reDrawTimer.Stop();
            reDrawTimer.Start();
            return;
        }

        try { RectanglesCanvas.Children.Remove(selectBorder); } catch { }

        await Task.Delay(50);
        CheckSelectBorderIntersections(true);
    }

    private void RectanglesCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isSelecting && !isMiddleDown)
            return;

        Point movingPoint = e.GetPosition(RectanglesCanvas);

        var left = Math.Min(clickedPoint.X, movingPoint.X);
        var top = Math.Min(clickedPoint.Y, movingPoint.Y);

        if (isMiddleDown)
        {
            double xShiftDelta = (movingPoint.X - clickedPoint.X);
            double yShiftDelta = (movingPoint.Y - clickedPoint.Y);

            Top += yShiftDelta;
            Left += xShiftDelta;

            return;
        }

        selectBorder.Height = Math.Max(clickedPoint.Y, movingPoint.Y) - top;
        selectBorder.Width = Math.Max(clickedPoint.X, movingPoint.X) - left;

        Canvas.SetLeft(selectBorder, left);
        Canvas.SetTop(selectBorder, top);

        CheckSelectBorderIntersections();
    }

    private void CheckSelectBorderIntersections(bool finalCheck = false)
    {
        Rect rectSelect = new Rect(Canvas.GetLeft(selectBorder), Canvas.GetTop(selectBorder), selectBorder.Width, selectBorder.Height);

        bool clickedEmptySpace = true;
        bool smallSelction = false;
        if (rectSelect.Width < 10 && rectSelect.Height < 10)
            smallSelction = true;

        foreach (WordBorder wordBorder in wordBorders)
        {
            Rect wbRect = new Rect(Canvas.GetLeft(wordBorder), Canvas.GetTop(wordBorder), wordBorder.Width, wordBorder.Height);

            if (rectSelect.IntersectsWith(wbRect))
            {
                clickedEmptySpace = false;

                if (smallSelction == false)
                {
                    wordBorder.Select();
                    wordBorder.WasRegionSelected = true;
                }
                else if (!finalCheck)
                {
                    if (wordBorder.IsSelected)
                        wordBorder.Deselect();
                    else
                        wordBorder.Select();
                    wordBorder.WasRegionSelected = false;
                }

            }
            else
            {
                if (wordBorder.WasRegionSelected == true
                    && smallSelction == false)
                    wordBorder.Deselect();
            }

            if (finalCheck == true)
                wordBorder.WasRegionSelected = false;
        }

        if (clickedEmptySpace == true
            && smallSelction == true
            && finalCheck == true)
        {
            foreach (WordBorder wb in wordBorders)
                wb.Deselect();
        }

        if (finalCheck)
            UpdateFrameText();
    }

    private void TableToggleButton_Click(object sender, RoutedEventArgs e)
    {
        TextBox searchBox = SearchBox;
        ResetGrabFrame();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private async void EditToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (EditToggleButton.IsChecked is bool isEditMode && isEditMode)
        {
            if (!IsFreezeMode)
            {
                FreezeToggleButton.IsChecked = true;
                ResetGrabFrame();
                await Task.Delay(200);
                FreezeGrabFrame();
                reDrawTimer.Stop();
                reDrawTimer.Start();
            }

            EnterEditMode();
        }
        else
            ExitEditMode();
    }

    private void EnterEditMode()
    {
        IsWordEditMode = true;

        foreach (UIElement uIElement in RectanglesCanvas.Children)
        {
            if (uIElement is WordBorder wb)
                wb.EnterEdit();
        }
    }

    private void ExitEditMode()
    {
        IsWordEditMode = false;

        foreach (UIElement uIElement in RectanglesCanvas.Children)
        {
            if (uIElement is WordBorder wb)
                wb.ExitEdit();
        }
    }

    private async void FreezeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        TextBox searchBox = SearchBox;
        ResetGrabFrame();

        await Task.Delay(200);
        if (FreezeToggleButton.IsChecked is bool freezeMode && freezeMode)
            FreezeGrabFrame();
        else
            UnfreezeGrabFrame();

        await Task.Delay(200);

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void FreezeGrabFrame()
    {
        if (droppedImageSource is not null)
            GrabFrameImage.Source = droppedImageSource;
        else if (frameContentImageSource is not null)
            GrabFrameImage.Source = frameContentImageSource;
        else
        {
            frameContentImageSource = ImageMethods.GetWindowBoundsImage(this);
            GrabFrameImage.Source = frameContentImageSource;
        }

        FreezeToggleButton.IsChecked = true;
        Topmost = false;
        this.Background = new SolidColorBrush(Colors.DimGray);
        RectanglesBorder.Background.Opacity = 0;
        IsFreezeMode = true;
    }

    private void UnfreezeGrabFrame()
    {
        Topmost = true;
        GrabFrameImage.Source = null;
        frameContentImageSource = null;
        droppedImageSource = null;
        RectanglesBorder.Background.Opacity = 0.05;
        FreezeToggleButton.IsChecked = false;
        this.Background = new SolidColorBrush(Colors.Transparent);
        IsFreezeMode = false;
    }

    private void EditTextBTN_Click(object sender, RoutedEventArgs e)
    {
        EditTextWindow etw = WindowUtilities.OpenOrActivateWindow<EditTextWindow>();
        DestinationTextBox = etw.GetMainTextBox();
        IsFromEditWindow = true;

        UpdateFrameText();
    }

    private async void GrabFrameWindow_Drop(object sender, DragEventArgs e)
    {
        // Mark the event as handled, so TextBox's native Drop handler is not called.
        e.Handled = true;
        var fileName = IsSingleFile(e);
        if (fileName is null) return;

        Activate();
        Uri fileURI = new(fileName);
        droppedImageSource = null;

        try
        {
            ResetGrabFrame();
            await Task.Delay(300);
            BitmapImage droppedImage = new(fileURI);
            droppedImageSource = droppedImage;
            FreezeToggleButton.IsChecked = true;
            FreezeGrabFrame();
        }
        catch (Exception)
        {
            UnfreezeGrabFrame();
            MessageBox.Show("Not an image");
        }

        IsDragOver = false;

        reDrawTimer.Start();
        reDrawTimer.Start();
    }

    private void GrabFrameWindow_DragOver(object sender, DragEventArgs e)
    {
        IsDragOver = true;
        // As an arbitrary design decision, we only want to deal with a single file.
        e.Effects = IsSingleFile(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        // Mark the event as handled, so TextBox's native DragOver handler is not called.
        e.Handled = true;
    }

    // If the data object in args is a single file, this method will return the filename.
    // Otherwise, it returns null.
    private static string? IsSingleFile(DragEventArgs args)
    {
        // Check for files in the hovering data object.
        if (args.Data.GetDataPresent(DataFormats.FileDrop, true))
        {
            var fileNames = args.Data.GetData(DataFormats.FileDrop, true) as string[];
            // Check for a single file or folder.
            if (fileNames?.Length is 1)
            {
                // Check for a file (a directory will return false).
                if (File.Exists(fileNames[0]))
                {
                    // At this point we know there is a single file.
                    return fileNames[0];
                }
            }
        }
        return null;
    }

    private void GrabFrameWindow_DragLeave(object sender, DragEventArgs e)
    {
        IsDragOver = false;
    }

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void OnRestoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
            this.WindowState = WindowState.Normal;
        else
            this.WindowState = WindowState.Maximized;

        SetRestoreState();
    }

    private void SetRestoreState()
    {
        if (WindowState == WindowState.Maximized)
            RestoreTextlock.Text = "";
        else
            RestoreTextlock.Text = "";
    }

    private void AspectRationMI_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem aspectMI)
            return;

        if (aspectMI.IsChecked == false)
            GrabFrameImage.Stretch = Stretch.Fill;
        else
            GrabFrameImage.Stretch = Stretch.Uniform;
    }

    private async void FreezeMI_Click(object sender, RoutedEventArgs e)
    {
        if (IsFreezeMode)
        {
            FreezeToggleButton.IsChecked = false;
            UnfreezeGrabFrame();
            ResetGrabFrame();
        }
        else
        {
            RectanglesCanvas.ContextMenu.IsOpen = false;
            await Task.Delay(150);
            FreezeToggleButton.IsChecked = true;
            ResetGrabFrame();
            FreezeGrabFrame();
        }

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void LoadOcrLanguages()
    {
        if (LanguagesComboBox.Items.Count > 0)
            return;

        IReadOnlyList<Language> possibleOCRLangs = OcrEngine.AvailableRecognizerLanguages;
        Language? firstLang = ImageMethods.GetOCRLanguage();

        int count = 0;

        foreach (Language language in possibleOCRLangs)
        {
            LanguagesComboBox.Items.Add(language);

            if (language.LanguageTag == firstLang?.LanguageTag)
                LanguagesComboBox.SelectedIndex = count;

            count++;
        }

        isLanguageBoxLoaded = true;
    }

    private void LanguagesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isLanguageBoxLoaded || sender is not ComboBox langComboBox)
            return;

        Language? pickedLang = langComboBox.SelectedItem as Language;

        if (pickedLang != null)
        {
            Settings.Default.LastUsedLang = pickedLang.LanguageTag;
            Settings.Default.Save();
        }

        ResetGrabFrame();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void LanguagesComboBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            Settings.Default.LastUsedLang = String.Empty;
            Settings.Default.Save();
        }
    }

    private void GrabFrameWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        FrameText = "";
        wordBorders.Clear();
        UpdateFrameText();
    }
}
