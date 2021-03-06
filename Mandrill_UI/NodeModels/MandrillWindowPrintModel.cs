﻿using Autodesk.DesignScript.Runtime;
using Dynamo.Controls;
using Dynamo.Graph.Nodes;
using Dynamo.UI.Commands;
using Dynamo.Wpf;
using System;
using System.Linq;
using System.Windows;
using HtmlAgilityPack;

namespace Mandrill.Print
{
    /// <summary>
    /// Prints Mandrill Report to PDF.
    /// </summary>
    [NodeName("Print to PDF")]
    [NodeCategory("Archi-lab_Mandrill.Report.Pdf")]
    [NodeDescription("Print Mandrill window to PDF.")]
    [IsDesignScriptCompatible]
    [InPortNames("FilePath", "Report", "Style")]
    [InPortDescriptions("A complete FilePath string including file extension.", "Mandrill Report containing all Charts.", "PDF Style that defines pdf size, orientation etc.")]
    [InPortTypes("String", "Report", "Style")]
    public class MandrillPrintNodeModel : NodeModel
    {
        private string _message;

        /// <summary>
        ///     Request save action.
        /// </summary>
        public Action RequestPrint;

        /// <summary>
        ///     A message that will appear on the button
        /// </summary>
        public string Message
        {
            get { return _message; }
            set
            {
                _message = value;
                RaisePropertyChanged("NodeMessage");
            }
        }

        /// <summary>
        ///     Delegate Command.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        public DelegateCommand MessageCommand { get; set; }

        /// <summary>
        ///     The constructor for a NodeModel is used to create
        ///     the input and output ports and specify the argument
        ///     lacing.
        /// </summary>
        public MandrillPrintNodeModel()
        {
            RegisterAllPorts();
            ArgumentLacing = LacingStrategy.Disabled;
            MessageCommand = new DelegateCommand(ShowMessage, CanShowMessage);
            Message = "   Print" + Environment.NewLine + "Window";
        }

        private static bool CanShowMessage(object obj)
        {
            return true;
        }

        private void ShowMessage(object obj)
        {
            this.RequestPrint();
        }

        /// <summary>
        ///     View customizer for CustomNodeModel Node Model.
        /// </summary>
        public class CustomNodeModelNodeViewCustomization : INodeViewCustomization<MandrillPrintNodeModel>
        {
            /// <summary>
            ///     Customization for Node View
            /// </summary>
            /// <param name="model">The NodeModel representing the node's core logic.</param>
            /// <param name="nodeView">The NodeView representing the node in the graph.</param>
            public void CustomizeView(MandrillPrintNodeModel model, NodeView nodeView)
            {
                var buttonControl = new MandrillPrintControl();
                nodeView.inputGrid.Width = 100;
                nodeView.inputGrid.Children.Add(buttonControl);
                buttonControl.DataContext = model;

                model.RequestPrint += () => PrintMandrillWindow(model, nodeView);
            }

            /// <summary>
            ///     Method that finds Mandrill Window and calls its Print() method.
            /// </summary>
            /// <param name="model"></param>
            /// <param name="nodeView"></param>
            public void PrintMandrillWindow(NodeModel model, NodeView nodeView)
            {
                string filePath;
                D3jsLib.Report report;
                D3jsLib.PdfStyle style;

                // collect inputs
                // prevent running if any input ports are empty
                if (model.InPorts.Any(x => x.Connectors.Count == 0))
                {
                    return;
                }
                else
                {
                    var graph = nodeView.ViewModel.DynamoViewModel.Model.CurrentWorkspace;

                    // process filePath input
                    var filePathNode = model.InPorts[0].Connectors[0].Start.Owner;
                    var filePathIndex = model.InPorts[0].Connectors[0].Start.Index;
                    var filePathId = filePathNode.GetAstIdentifierForOutputIndex(filePathIndex).Name;
                    var filePathMirror = nodeView.ViewModel.DynamoViewModel.Model.EngineController.GetMirror(filePathId);
                    filePath = filePathMirror.GetData().Data as string;

                    // process report input
                    var reportNode = model.InPorts[1].Connectors[0].Start.Owner;
                    var reportIndex = model.InPorts[1].Connectors[0].Start.Index;
                    var reportId = reportNode.GetAstIdentifierForOutputIndex(reportIndex).Name;
                    var reportMirror = nodeView.ViewModel.DynamoViewModel.Model.EngineController.GetMirror(reportId);
                    report = reportMirror.GetData().Data as D3jsLib.Report;

                    // process style input
                    var styleNode = model.InPorts[2].Connectors[0].Start.Owner;
                    var styleIndex = model.InPorts[2].Connectors[0].Start.Index;
                    var styleId = styleNode.GetAstIdentifierForOutputIndex(styleIndex).Name;
                    var styleMirror = nodeView.ViewModel.DynamoViewModel.Model.EngineController.GetMirror(styleId);
                    style = styleMirror.GetData().Data as D3jsLib.PdfStyle;
                }

                // print PDF
                this.PrintPDF(report, style, filePath);
            }

            private void PrintPDF(D3jsLib.Report report, D3jsLib.PdfStyle style, string filePath)
            {
                HtmlDocument htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(report.HtmlString);

                HtmlNodeCollection nodes = htmlDoc.DocumentNode.SelectNodes("//div[@class='gridster-box']");
                foreach (HtmlNode n in nodes)
                {
                    n.InnerHtml = "";
                }

                // attempt to move *dep file
                D3jsLib.Utilities.ChartsUtilities.MoveDepFile();

                // create converter
                SelectPdf.HtmlToPdf converter = new SelectPdf.HtmlToPdf();

                // set converter options
                SelectPdf.HtmlToPdfOptions options = converter.Options;
                options.PdfPageOrientation = style.Orientation;
                options.PdfPageSize = style.Size;
                options.JpegCompressionLevel = style.Compression;
                options.JavaScriptEnabled = true;
                options.EmbedFonts = true;
                options.KeepImagesTogether = true;
                options.KeepTextsTogether = true;
                options.AutoFitHeight = style.VerticalFit;
                options.AutoFitWidth = style.HorizontalFit;
                options.MarginTop = style.MarginTop;
                options.MarginRight = style.MarginRight;
                options.MarginBottom = style.MarginBottom;
                options.MarginLeft = style.MarginLeft;

                // created unescaped file path removes %20 from path etc.
                string finalFilePath = filePath;

                Uri uri = new Uri(filePath);
                string absoluteFilePath = Uri.UnescapeDataString(uri.AbsoluteUri);

                if (Uri.IsWellFormedUriString(absoluteFilePath, UriKind.RelativeOrAbsolute))
                {
                    Uri newUri = new Uri(absoluteFilePath);
                    finalFilePath = newUri.LocalPath;
                }

                try
                {
                    // convert html to document object and save
                    SelectPdf.PdfDocument pdfDoc = converter.ConvertHtmlString(htmlDoc.DocumentNode.InnerHtml);
                    pdfDoc.Save(finalFilePath);
                    pdfDoc.Close();
                }
                catch
                {
                    MessageBox.Show("Printing failed. Is file open in another application?");
                }
            }

            /// <summary>
            ///     Dispose of model.
            /// </summary>
            public void Dispose()
            {
            }
        }
    }
}