using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Compression;
using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using System.Windows.Forms.VisualStyles;
using Microsoft.VisualBasic;
using Zuby.ADGV;
using Microsoft.VisualBasic.Logging;
using System.Data.SqlClient;

namespace HiResLogViewer
{
    public partial class frmMain : Form
    {
        private byte[] _ZlibHeaderNoCompression = new byte[] { 120, 1 };
        private byte[] _ZlibHeaderDefaultCompression = new byte[] { 120, 156 };
        private byte[] _ZlibHeaderBestCompression = new byte[] { 120, 218 };
        private byte[] _GZipHeader = new byte[] { 31, 139 };

        public List<ControllerEvent> EventLog = new List<ControllerEvent>();

        public frmMain()
        {
            InitializeComponent();
        }

        private void cutToolStripButton_Click(object sender, EventArgs e)
        {

        }

        private void openToolStripButton_Click(object sender, EventArgs e)
        {
            DateTime intervalStart;
            string originalFilename, intNum;
            double versionNumber;

            OpenFileDialog openFileDialog = new OpenFileDialog()
            {
                Title = "Select files (press CTRL to select multiple files)",
                Filter = "High resolution log files (*.dat;*.datZ)|*.dat;*.datZ",
                Multiselect = true
            };
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {

                foreach(string inputFile in openFileDialog.FileNames)
                {
                    string fileType;
                    string fileExt;
                    string[] filenameWords;

                    originalFilename = Path.GetFileName(inputFile);
                    fileExt = Path.GetExtension(inputFile);
                    filenameWords = originalFilename.Split('_');

                    fileType = filenameWords[0];        //ECON or INT or MCCN or SIEM

                    Encoding fileEncoding = Encoding.ASCII;

                    FileStream fileStream = File.Open(inputFile, FileMode.Open);
                    MemoryStream memoryStream = new MemoryStream();
                    fileStream.CopyTo(memoryStream);
                    fileStream.Close();

                    //set the memory position to the beginning
                    memoryStream.Position = 0;

                    //decompress econolite datZ files
                    if (fileExt == ".datZ" && fileType == "ECON")
                    {   
                        //check if data is compressed
                        if (IsCompressed(memoryStream))
                        {
                            memoryStream = DecompressedStream(memoryStream);
                        }
                    }

                    //determine file start date
                    if (fileType == "MCCN" && filenameWords.Length == 4)
                    {
                        //new McCain filenaming format in firmware v1.10
                        //format is MCCN_ID_YYYYMMDD_HHMMSS.dat
                        intervalStart = DateTime.Parse(filenameWords[2].Substring(4,2) + "/" + filenameWords[2].Substring(6,2) + "/" + filenameWords[2].Substring(0,4) + " " + filenameWords[3].Substring(0,2) +":" + filenameWords[3].Substring(2, 2) + filenameWords[3].Substring(4, 2));
                    }
                    else
                    {
                        //date separated by underscore
                        intervalStart = DateTime.Parse(filenameWords[3] + "/" + filenameWords[4] + "/" + filenameWords[2] + " " + filenameWords[5].Substring(0, 2) + ":" + filenameWords[5].Substring(2, 2));
                    }

                    //Siemens files bypass the code below and go to the Siemens decoder
                    if (fileType == "SIEM")
                    {
                        string decoder = "PerfLogTranslate.exe";

                        int timeout = 2000;

                        try
                        {
                            //set the current directory. one of the quirks of the decoder is that 
                            //it requires the target files to be in the current working directory
                            Directory.SetCurrentDirectory(Path.GetDirectoryName(inputFile));

                            string arguments = "-i " + originalFilename;
                            ProcessStartInfo processInfo = new ProcessStartInfo();
                            processInfo.Arguments = arguments;
                            processInfo.FileName = decoder;
                            processInfo.CreateNoWindow = true;
                            processInfo.UseShellExecute = false;
                            processInfo.RedirectStandardError = false;
                            processInfo.RedirectStandardOutput = false;
                            Process p = Process.Start(processInfo);

                            // wait for the process to exit or timeout
                            p.WaitForExit(timeout);

                            // check to see if the process is still running
                            if (p.HasExited == false)
                            {
                                // process is still running
                                // test to see if the process is hung up
                                if (p.Responding)
                                    // process was responding; close the main window
                                    p.CloseMainWindow();
                                else
                                    // process was not responding; force the process to close
                                    p.Kill();
                            }
                            p.Dispose();
                        }
                        catch(Exception ex)
                        {
                            break;
                        }

                        //read the CSV file
                        byte EventCode, EventParameter;
                        DateTime EventTime;

                        TextFieldParser CSVreader = new TextFieldParser(inputFile.Replace(".dat", ".csv"));
                        CSVreader.TextFieldType = FieldType.Delimited;
                        CSVreader.Delimiters = new string[] { "," };

                        string[] Words;
                        while (!CSVreader.EndOfData)
                        {
                            Words = CSVreader.ReadFields();
                            EventTime = DateTime.Parse(Words[0]);

                            //m50 uses UTC time and decoder doesn't fix it, m60 seems to have fixed this
                            if (EventTime.Hour != intervalStart.Hour)
                                EventTime = TimeZoneInfo.ConvertTimeToUtc(EventTime, TimeZoneInfo.Local);
                            EventCode = System.Convert.ToByte(Words[1]);
                            EventParameter = System.Convert.ToByte(Words[2]);

                            EventLog.Add(new ControllerEvent(1, EventTime, EventCode, EventParameter));
                        }

                        //delete the temporary CSV file
                        CSVreader.Close();
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(inputFile.Replace(".dat", ".csv"));
                    }
                    else if (fileType == "TRAF")
                    {
                        string decoder = "PurdueDecoder.exe";

                        int timeout = 2000;

                        try
                        {
                            //set the current directory
                            Directory.SetCurrentDirectory(Path.GetDirectoryName(inputFile));

                            string arguments = originalFilename + " " + originalFilename.Replace(".dat", ".csv");
                            ProcessStartInfo processInfo = new ProcessStartInfo();
                            processInfo.Arguments = arguments;
                            processInfo.FileName = decoder;
                            processInfo.CreateNoWindow = true;
                            processInfo.UseShellExecute = false;
                            processInfo.RedirectStandardError = false;
                            processInfo.RedirectStandardOutput = false;

                            Process p = Process.Start(processInfo);

                            // wait for the process to exit or timeout
                            p.WaitForExit(timeout);

                            // check to see if the process is still running
                            if (p.HasExited == false)
                            {
                                if (p.Responding)
                                    p.CloseMainWindow();
                                else
                                    p.Kill();
                            }
                            p.Dispose();
                        }
                        catch(Exception ex)
                        {
                            break;
                        }

                        //read the CSV file and import it
                        byte EventCode, EventParameter;
                        DateTime EventTime;

                        TextFieldParser CSVreader = new TextFieldParser(inputFile.Replace(".dat", ".csv"));
                        CSVreader.TextFieldType = FieldType.Delimited;
                        CSVreader.Delimiters = new string[] { "," };

                        //ignore the first six lines which are headers
                        for (int i=1; i<=6; i++)
                        {
                            CSVreader.ReadLine();
                        }

                        string[] Words;
                        while (!CSVreader.EndOfData)
                        {
                            Words = CSVreader.ReadFields();
                            EventTime = DateTime.Parse(Words[0]);
                            EventCode = System.Convert.ToByte(Words[1]);
                            EventParameter = System.Convert.ToByte(Words[2]);

                            EventLog.Add(new ControllerEvent(1, EventTime, EventCode, EventParameter));
                        }

                        //delete the temporary CSV file
                        CSVreader.Close();
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(inputFile.Replace(".dat", ".csv"));
                    }
                    else
                    {
                        var binReader = new BinaryReader(memoryStream);
                        byte tempByte;
                        int HeaderLineCount;
                        byte EventCode, EventParameter;
                        int TimerTenths, timerA, timerB;
                        DateTime eventTime, priorTime;
                        byte NumHeaders;

                        priorTime = intervalStart.AddSeconds(-1);

                        HeaderLineCount = 0;

                        if (fileType == "ECON")
                            NumHeaders = 7;
                        else if (fileType == "INT")
                            NumHeaders = 6;
                        else
                            NumHeaders = 0;

                        //pass through header lines to get to binary data
                        if (fileType == "ECON" || fileType == "INT")
                        {
                            while (HeaderLineCount < NumHeaders && (binReader.BaseStream.Position < binReader.BaseStream.Length))
                            {
                                try
                                {
                                    tempByte = binReader.ReadByte();
                                    if (tempByte == 10)
                                        HeaderLineCount += 1;       //10 = line feed character "\n"
                                }
                                catch (Exception ex)
                                {
                                    break;
                                }
                            }

                            // 4 bytes per record
                            // byte 1 = event code
                            // byte 2 = event parameter
                            // bytes 3 & 4 = 10th of a second from the beginning of the file interval (1 hour for INT and 15 minutes, :00, :15, :30, :45 for ECON)
                            while ((binReader.BaseStream.Position + 4 ) <= binReader.BaseStream.Length)
                            {
                                try
                                {
                                    EventCode = binReader.ReadByte();
                                    EventParameter = binReader.ReadByte();
                                    timerA = binReader.ReadByte();
                                    timerB = binReader.ReadByte();
                                    TimerTenths = timerA * 256 + timerB;
                                    eventTime = intervalStart.AddMilliseconds(TimerTenths * 100);
                                    if (eventTime < priorTime & TimerTenths == 0)
                                    {
                                        if (fileType == "ECON")
                                            eventTime = intervalStart.AddMinutes(15);       //this is really the end of the interval not the beginning
                                        else if (fileType == "INT")
                                            eventTime = intervalStart.AddMinutes(60);
                                    }
                                    else
                                        priorTime = eventTime;      //set prior time for the next iteration

                                    if (fileType == "INT")
                                    {
                                        //skip junk lines from INT files
                                        if ((EventCode == 0 | EventCode == 1) & (EventParameter == 0 | EventParameter > 16))
                                            continue;       // event code 0 and 1 are for phase events, must have a phase number, not zero and not 255 (can't be > 16 per InDOT)
                                        if (EventCode == 0 & EventParameter == 1 & timerA == 0 & timerB == 0)
                                            continue;     // There are usually two of these at the beginning
                                        if (EventCode == 2 & EventParameter == 2 & timerA == 2 & timerB == 2)
                                            continue;
                                        if (TimerTenths > 36000)
                                            continue;   // file intervals one hour, this would be more than an hour, so it's junk

                                        // translate event code for INT files
                                        EventCode = TranslateOldEconolite(EventCode);
                                    }

                                    EventLog.Add(new ControllerEvent(1, eventTime, EventCode, EventParameter));
                                }
                                catch(Exception ex)
                                {
                                    break;
                                }
                            }
                        }
                        else if (fileType == "MCCN")
                        {
                            binReader.BaseStream.Position = 80;
                            while ((binReader.BaseStream.Position + 4 ) <= binReader.BaseStream.Length)
                            {
                                try
                                {
                                    timerA = binReader.ReadByte();
                                    timerB = binReader.ReadByte();
                                    EventCode = binReader.ReadByte();
                                    EventParameter = binReader.ReadByte();

                                    TimerTenths = timerA * 256 + timerB;
                                    eventTime = intervalStart.AddMilliseconds(TimerTenths * 100);

                                    //skip junk lines
                                    if (EventCode == 0 && EventParameter == 0 && TimerTenths == 0)
                                        continue;

                                    EventLog.Add(new ControllerEvent(1, eventTime, EventCode, EventParameter));
                                }
                                catch(Exception ex)
                                {
                                    break;
                                }
                            }
                        }
                        binReader.Close();
                    }

                }

                cmdDataSelect.SelectedItem = "All Log Data";

                
            }
        }

        private bool IsCompressed(MemoryStream fileStream)
        {
            //read the magic header
            byte[] header = new byte[2];

            fileStream.Read(header, 0, 2);

            //let seek to back of file
            fileStream.Seek(0, SeekOrigin.Begin);

            //let's check for zLib compression
            if (AreEqual(header, _ZlibHeaderNoCompression) || AreEqual(header, _ZlibHeaderDefaultCompression) || AreEqual(header, _ZlibHeaderBestCompression))
            {
                return true;
            }
            else if (AreEqual(header, _GZipHeader))
            {
                return true;
            }
            return false;
        }

        private bool AreEqual(byte[] a1, byte[] b1)
        {
            if (a1.Length != b1.Length) return false;

            for (int i = 0; i <= (a1.Length - 1); i++)
            {
                if (a1[i] != b1[i]) return false;
            }

            return true;
        }

        private MemoryStream DecompressedStream(MemoryStream fileStream)
        {
            //read past the first two bytes of the zlib header
            fileStream.Seek(2, SeekOrigin.Begin);

            //decompress the file
            using (DeflateStream deflatedStream = new DeflateStream(fileStream, CompressionMode.Decompress))
            {
                //copy decompressed data into return stream
                MemoryStream returnStream = new MemoryStream();
                deflatedStream.CopyTo(returnStream);
                returnStream.Position = 0;

                return returnStream;
            }
        }

        private byte TranslateOldEconolite(byte OldCode)
        {
            byte e;
            switch (OldCode)
            {
                case 0:  // Phase off
                    {
                        e = 12;
                        break;
                    }

                case 1:  // Phase green
                    {
                        e = 1;
                        break;
                    }

                case 2:  // Phase yellow
                    {
                        e = 8;
                        break;
                    }

                case 3:  // Phase red clear
                    {
                        e = 10;
                        break;
                    }

                case 4:  // Ped off
                    {
                        e = 23;
                        break;
                    }

                case 5:  // Ped walk
                    {
                        e = 21;
                        break;
                    }

                case 6:  // Ped clear
                    {
                        e = 22;
                        break;
                    }

                case 8:  // Detector off
                    {
                        e = 81;
                        break;
                    }

                case 9:  // detector on
                    {
                        e = 82;
                        break;
                    }

                case 12: // overlap off
                    {
                        e = 65;
                        break;
                    }

                case 13: // overlap green
                    {
                        e = 61;
                        break;
                    }

                case 14: // overlap green extension
                    {
                        e = 62;
                        break;
                    }

                case 15: // overlap yellow
                    {
                        e = 63;
                        break;
                    }

                case 16: // overlap red clear
                    {
                        e = 64;
                        break;
                    }

                case 20: // preempt active
                    {
                        e = 102;
                        break;
                    }

                case 21: // preempt off
                    {
                        e = 104;
                        break;
                    }

                case 24: // phase hold active
                    {
                        e = 41;
                        break;
                    }

                case 25: // phase hold released
                    {
                        e = 42;
                        break;
                    }

                case 26: // ped call on phase
                    {
                        e = 45;
                        break;
                    }

                case 27: // ped call cleared (this event doesn't exist in new enumerations, just call it phase call dropped
                    {
                        e = 44;
                        break;
                    }

                case 32: // phase min complete
                    {
                        e = 3;
                        break;
                    }

                case 33: // phase termination gap out
                    {
                        e = 4;
                        break;
                    }

                case 34: // phase temrination max out
                    {
                        e = 5;
                        break;
                    }

                case 35: // phase termination force off
                    {
                        e = 6;
                        break;
                    }

                case 40: // coord pattern change
                    {
                        e = 131;
                        break;
                    }

                case 41: // cycle length change
                    {
                        e = 132;
                        break;
                    }

                case 42: // offset length change
                    {
                        e = 133;
                        break;
                    }

                case 43: // split 1 change
                    {
                        e = 134;
                        break;
                    }

                case 44: // split 2 change
                    {
                        e = 135;
                        break;
                    }

                case 45: // split 3 change
                    {
                        e = 136;
                        break;
                    }

                case 46: // split 4 change
                    {
                        e = 137;
                        break;
                    }

                case 47: // split 5 change
                    {
                        e = 138;
                        break;
                    }

                case 48: // split 6 change
                    {
                        e = 139;
                        break;
                    }

                case 49: // split 7 change
                    {
                        e = 140;
                        break;
                    }

                case 50: // split 8 change
                    {
                        e = 141;
                        break;
                    }

                case 51: // split 9 change
                    {
                        e = 142;
                        break;
                    }

                case 52: // split 10 change
                    {
                        e = 143;
                        break;
                    }

                case 53: // split 11 change
                    {
                        e = 144;
                        break;
                    }

                case 54: // split 12 change
                    {
                        e = 145;
                        break;
                    }

                case 55: // split 13 change
                    {
                        e = 146;
                        break;
                    }

                case 56: // split 14 change
                    {
                        e = 147;
                        break;
                    }

                case 57: // split 15 change
                    {
                        e = 148;
                        break;
                    }

                case 58: // split 16 change
                    {
                        e = 149;
                        break;
                    }

                case 62: // coord cycle state change
                    {
                        e = 150;
                        break;
                    }

                case 63: // coord phase yield point
                    {
                        e = 151;
                        break;
                    }

                default:
                    {
                        e = 255;
                        break;
                    }
            }
            return e;
        }

        private void FormatDataLog(AdvancedDataGridView dgv)
        {
            // set background colors for phase events
            if ((string)cmdDataSelect.SelectedItem == "All Log Data")
            {
                foreach (DataGridViewRow myRow in dgv.Rows)
                {
                    switch (myRow.Cells[1].Value)
                    {
                        case "Phase Begin Green":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.LightGreen;
                                break;
                            }

                        case "Phase Begin Yellow Clearance":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.Gold;
                                break;
                            }

                        case "Phase Begin Red Clearance":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.Firebrick;
                                myRow.DefaultCellStyle.ForeColor = Color.White;
                                break;
                            }

                        case "Pedestrian Begin Walk":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.Black;
                                myRow.DefaultCellStyle.ForeColor = Color.White;
                                break;
                            }

                        case "Pedestrian Begin Clearance":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.Orange;
                                break;
                            }

                        case "Detector Off":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.PowderBlue;
                                break;
                            }

                        case "Detector On":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.RoyalBlue;
                                myRow.DefaultCellStyle.ForeColor = Color.White;
                                break;
                            }

                        case "Phase Omit Off":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.SlateGray;
                                myRow.DefaultCellStyle.ForeColor = Color.White;
                                break;
                            }

                        case "Phase Omit On":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.LightSlateGray;
                                break;
                            }

                        case "Phase Call Registered":
                            {
                                break;
                            }

                        case "Phase Call Dropped":
                            {
                                break;
                            }

                        case "Coord cycle state change":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.Indigo;
                                myRow.DefaultCellStyle.ForeColor = Color.White;
                                break;
                            }

                        case "Unit Flash Status change":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.Khaki;
                                myRow.DefaultCellStyle.ForeColor = Color.Firebrick;
                                break;
                            }

                        case "Power Failure Detected":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.Gray;
                                myRow.DefaultCellStyle.ForeColor = Color.Firebrick;
                                break;
                            }

                        case "Unit Alarm Status 1 change":
                            {
                                myRow.DefaultCellStyle.BackColor = Color.Fuchsia;
                                break;
                            }
                    }
                }
            }
        }

        private void logDgv_FilterStringChanged(object sender, AdvancedDataGridView.FilterEventArgs e)
        {
            FormatDataLog(logDgv);
        }

        private void copyToolStripButton_Click(object sender, EventArgs e)
        {
            string dgvToHtmlTable = ConvertDataGridViewToHTMLWithFormatting(logDgv);
            Clipboard.SetText(dgvToHtmlTable);
        }

        public string ConvertDataGridViewToHTMLWithFormatting(DataGridView dgv)
        {
            StringBuilder sb = new StringBuilder();

            // create html & table
            sb.AppendLine("<html><body><center><table border='1' cellpadding='0' cellspacing='0'>");
            sb.AppendLine("<tr>");

            // create table header
            for (var i = 0; i <= dgv.Columns.Count - 1; i++)
            {
                sb.Append(DGVHeadercellToHTMLWithFormatting(dgv, i));
                sb.Append(DGVCellFontAndValueToHTML(dgv.Columns[i].HeaderText, dgv.Columns[i].HeaderCell.Style.Font));
                sb.AppendLine("</td>");
            }
            sb.AppendLine("</tr>");

            // create table body
            for (var rowIndex = 0; rowIndex <= dgv.Rows.Count - 1; rowIndex++)
            {
                sb.AppendLine("<tr>");
                foreach (DataGridViewCell dgvc in dgv.Rows[rowIndex].Cells)
                {
                    sb.AppendLine(DGVCellToHTMLWithFormatting(dgv, rowIndex, dgvc.ColumnIndex));
                    string cellValue = dgvc.FormattedValue == null ? string.Empty : (string)dgvc.FormattedValue;
                    sb.AppendLine(DGVCellFontAndValueToHTML(cellValue, dgvc.Style.Font));
                    sb.AppendLine("</td>");
                }
                sb.AppendLine("</tr>");
            }

            // table footer & end of html file
            sb.AppendLine("</table></center></body></html>");
            return sb.ToString();
        }

        private string DGVHeadercellToHTMLWithFormatting(DataGridView dgv, int col)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<td");
            sb.Append(DGVCellColorToHTML(dgv.Columns[col].HeaderCell.Style.ForeColor, dgv.Columns[col].HeaderCell.Style.BackColor));
            sb.Append(DGVCellAlignmentToHTML(dgv.Columns[col].HeaderCell.Style.Alignment));
            sb.Append(">");
            return sb.ToString();
        }

        private string DGVCellToHTMLWithFormatting(DataGridView dgv, int row, int col)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<td");
            sb.Append(DGVCellColorToHTML(dgv.Rows[row].DefaultCellStyle.ForeColor, dgv.Rows[row].DefaultCellStyle.BackColor));
            sb.Append(DGVCellAlignmentToHTML(dgv.Rows[row].DefaultCellStyle.Alignment));
            // sb.Append(DGVCellColorToHTML(dgv.Rows(row).Cells(col).Style.ForeColor, dgv.Rows(row).Cells(col).Style.BackColor))
            // sb.Append(DGVCellAlignmentToHTML(dgv.Rows(row).Cells(col).Style.Alignment))
            sb.Append(">");
            return sb.ToString();
        }

        private string DGVCellColorToHTML(Color foreColor, Color backColor)
        {
            if ((foreColor.Name == "0" & backColor.Name == "0"))
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.Append(" style=\"");
            if (foreColor.Name != "0" & backColor.Name != "0")
            {
                sb.Append("color:#");
                sb.Append(foreColor.R.ToString("X2") + foreColor.G.ToString("X2") + foreColor.B.ToString("X2"));
                sb.Append("; background-color:#");
                sb.Append(backColor.R.ToString("X2") + backColor.G.ToString("X2") + backColor.B.ToString("X2"));
            }
            else if (foreColor.Name != "0" & backColor.Name == "0")
            {
                sb.Append("color:#");
                sb.Append(foreColor.R.ToString("X2") + foreColor.G.ToString("X2") + foreColor.B.ToString("X2"));
            }
            else
            {
                sb.Append("background-color:#");
                sb.Append(backColor.R.ToString("X2") + backColor.G.ToString("X2") + backColor.B.ToString("X2"));
            }
            sb.Append(";\"");
            return sb.ToString();
        }

        private string DGVCellFontAndValueToHTML(string value, Font font)
        {
            Font defaultFont = new Font("MS Sans Serif", 8.25f, FontStyle.Regular, GraphicsUnit.Point);

            // if no font has been set then assume its default as someone would be expected in HTML or Excel
            if (font == null || (font.Name == defaultFont.Name & !(font.Bold | font.Italic | font.Underline)))
                return value;

            StringBuilder sb = new StringBuilder();
            sb.Append(" ");
            if (font.Bold)
                sb.Append("<b>");
            if (font.Italic)
                sb.Append("<i>");
            if (font.Strikeout)
                sb.Append("<strike>");

            // The <u> element was deprecated in HTML 4.01. The new HTML 5 tag is: text-decoration: underline
            if (font.Underline)
                sb.Append("<u>");

            string size = string.Empty;
            if (font.Size != defaultFont.Size)
                size = "font-size: " + font.Size + "pt;";

            // the <font> tag is not supported in HTML5. Use CSS or a span instead
            if (font.FontFamily.Name != defaultFont.FontFamily.Name)
            {
                sb.Append("<span style=\"font-family: ");
                sb.Append(font.FontFamily.Name);
                sb.Append("; ");
                sb.Append(size);
                sb.Append("\">");
            }
            sb.Append(value);

            if (font.FontFamily.Name != defaultFont.FontFamily.Name)
                sb.Append("</span>");

            if (font.Underline)
                sb.Append("</u>");
            if (font.Strikeout)
                sb.Append("</strike>");
            if (font.Italic)
                sb.Append("</i>");
            if (font.Bold)
                sb.Append("</b>");

            return sb.ToString();
        }

        private string DGVCellAlignmentToHTML(DataGridViewContentAlignment align)
        {
            if (align == DataGridViewContentAlignment.NotSet)
                return string.Empty;

            string horizontalAlignment = string.Empty;
            string verticalAlignment = string.Empty;
            CellAlignment(align, horizontalAlignment, verticalAlignment);
            StringBuilder sb = new StringBuilder();
            sb.Append(" align='");
            sb.Append(horizontalAlignment);
            sb.Append("' valign='");
            sb.Append(verticalAlignment);
            sb.Append("'");
            return sb.ToString();
        }

        private void CellAlignment(DataGridViewContentAlignment align, string horizontalAlignment, string verticalAlignment)
        {
            switch (align)
            {
                case DataGridViewContentAlignment.MiddleRight:
                    {
                        horizontalAlignment = "right";
                        verticalAlignment = "middle";
                        break;
                    }

                case DataGridViewContentAlignment.MiddleLeft:
                    {
                        horizontalAlignment = "left";
                        verticalAlignment = "middle";
                        break;
                    }

                case DataGridViewContentAlignment.MiddleCenter:
                    {
                        horizontalAlignment = "center";
                        verticalAlignment = "middle";
                        break;
                    }

                case DataGridViewContentAlignment.TopCenter:
                    {
                        horizontalAlignment = "center";
                        verticalAlignment = "top";
                        break;
                    }

                case DataGridViewContentAlignment.BottomCenter:
                    {
                        horizontalAlignment = "center";
                        verticalAlignment = "bottom";
                        break;
                    }

                case  DataGridViewContentAlignment.TopLeft:
                    {
                        horizontalAlignment = "left";
                        verticalAlignment = "top";
                        break;
                    }

                case  DataGridViewContentAlignment.BottomLeft:
                    {
                        horizontalAlignment = "left";
                        verticalAlignment = "bottom";
                        break;
                    }

                case DataGridViewContentAlignment.TopRight:
                    {
                        horizontalAlignment = "right";
                        verticalAlignment = "top";
                        break;
                    }

                case DataGridViewContentAlignment.BottomRight:
                    {
                        horizontalAlignment = "right";
                        verticalAlignment = "bottom";
                        break;
                    }

                default:
                    {
                        horizontalAlignment = "left";
                        verticalAlignment = "middle";
                        break;
                    }
            }
        }

        private void logDgv_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            FormatDataLog(logDgv);
        }

        private void cmdDataSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch(cmdDataSelect.SelectedItem)
            {
                case "All Log Data":
                    DisplayLogData();
                    break;
                case "Coodination Plans":
                    //DisplayCoordData();
                    break;
                case "Cycle Data":
                    //DisplayCycleData();
                    break;
                case "Volumes":
                    //DisplayVolumes();
                    break;
                default:
                    break;
            }
        }

        private void DisplayLogData()
        {
            logDgv.DataSource = null;

            DataTable logTable = new DataTable();

            DataSet ds = new DataSet();

            BindingSource bs1 = new BindingSource();
            bs1.DataSource = ds;

            logTable = ds.Tables.Add("All Log Data");
            logTable.Columns.Add("Event Code", typeof(byte));
            logTable.Columns.Add("Event Description", typeof(string));
            logTable.Columns.Add("Event Parameter", typeof(byte));
            logTable.Columns.Add("Timestamp", typeof(DateTime));

            bs1.DataMember = logTable.TableName;

            foreach(ControllerEvent logRow in EventLog)
            {
                logTable.Rows.Add(logRow.EventCode.ToString(), GetEventCodeDescriptor(logRow.EventCode), logRow.EventParam.ToString(), logRow.TimeStamp.ToString("MM/dd/yyyy hh:mm:ss.f tt"));
            }

            logDgv.DataSource = logTable;
            logDgv.Columns["Timestamp"].DefaultCellStyle.Format = "MM/dd/yyyy hh:mm:ss.f tt";
            logDgv.SetFilterDateAndTimeEnabled(logDgv.Columns["Timestamp"], true);

            logDgv.Columns["Event Code"].Width = 81;
            logDgv.Columns["Event Description"].Width = 250;
            logDgv.Columns["Event Parameter"].Width = 100;
            logDgv.Columns["Timestamp"].Width = 150;
            FormatDataLog(logDgv);
        }

        public string GetEventCodeDescriptor(byte EventCode)
        {
            string Desc;

            switch (EventCode)
            {
                case 0:
                    {
                        Desc = "Phase On";
                        break;
                    }

                case 1:
                    {
                        Desc = "Phase Begin Green";
                        break;
                    }

                case 2:
                    {
                        Desc = "Phase Check";
                        break;
                    }

                case 3:
                    {
                        Desc = "Phase Min Complete";
                        break;
                    }

                case 4:
                    {
                        Desc = "Phase Gap Out";
                        break;
                    }

                case 5:
                    {
                        Desc = "Phase Max Out";
                        break;
                    }

                case 6:
                    {
                        Desc = "Phase Force Off";
                        break;
                    }

                case 7:
                    {
                        Desc = "Phase Green Termination";
                        break;
                    }

                case 8:
                    {
                        Desc = "Phase Begin Yellow Clearance";
                        break;
                    }

                case 9:
                    {
                        Desc = "Phase End Yellow Clearance";
                        break;
                    }

                case 10:
                    {
                        Desc = "Phase Begin Red Clearance";
                        break;
                    }

                case 11:
                    {
                        Desc = "Phase End Red Clearance";
                        break;
                    }

                case 12:
                    {
                        Desc = "Phase Inactive";
                        break;
                    }

                case 21:
                    {
                        Desc = "Pedestrian Begin Walk";
                        break;
                    }

                case 22:
                    {
                        Desc = "Pedestrian Begin Clearance";
                        break;
                    }

                case 23:
                    {
                        Desc = "Pedestrian Begin Solid Don't Walk";
                        break;
                    }

                case 24:
                    {
                        Desc = "Pedestrian Dark";
                        break;
                    }

                case 31:
                    {
                        Desc = "Barrier Termination";
                        break;
                    }

                case 32:
                    {
                        Desc = "FYA - Begin Permissive";
                        break;
                    }

                case 33:
                    {
                        Desc = "FYA - End Permissive";
                        break;
                    }

                case 41:
                    {
                        Desc = "Phase Hold Active";
                        break;
                    }

                case 42:
                    {
                        Desc = "Phase Hold Released";
                        break;
                    }

                case 43:
                    {
                        Desc = "Phase Call Registered";
                        break;
                    }

                case 44:
                    {
                        Desc = "Phase Call Dropped";
                        break;
                    }

                case 45:
                    {
                        Desc = "Pedestrian Call Registered";
                        break;
                    }

                case 46:
                    {
                        Desc = "Phase Omit On";
                        break;
                    }

                case 47:
                    {
                        Desc = "Phase Omit Off";
                        break;
                    }

                case 48:
                    {
                        Desc = "Pedestrian Omit On";
                        break;
                    }

                case 49:
                    {
                        Desc = "Pedestrian Omit Off";
                        break;
                    }

                case 61:
                    {
                        Desc = "Overlap Begin Green";
                        break;
                    }

                case 62:
                    {
                        Desc = "Overlap Begin Trailing Green (Extension)";
                        break;
                    }

                case 63:
                    {
                        Desc = "Overlap Begin Yellow";
                        break;
                    }

                case 64:
                    {
                        Desc = "Overlap Begin Red Clearance";
                        break;
                    }

                case 65:
                    {
                        Desc = "Overlap Off (Inactive with red indication)";
                        break;
                    }

                case 66:
                    {
                        Desc = "Overlap Dark";
                        break;
                    }

                case 67:
                    {
                        Desc = "Pedestrian Overlap Begin Walk";
                        break;
                    }

                case 68:
                    {
                        Desc = "Pedestrian Overlap Begin Clearance";
                        break;
                    }

                case 69:
                    {
                        Desc = "Pedestrian Overlap Begin Solid Don't Walk";
                        break;
                    }

                case 70:
                    {
                        Desc = "Pedestrian Overlap Dark";
                        break;
                    }

                case 81:
                    {
                        Desc = "Detector Off";
                        break;
                    }

                case 82:
                    {
                        Desc = "Detector On";
                        break;
                    }

                case 83:
                    {
                        Desc = "Detector Restored";
                        break;
                    }

                case 84:
                    {
                        Desc = "Detector Fault-Other";
                        break;
                    }

                case 85:
                    {
                        Desc = "Detector Fault-Watchdog Fault";
                        break;
                    }

                case 86:
                    {
                        Desc = "Detector Fault-Open Loop Fault";
                        break;
                    }

                case 87:
                    {
                        Desc = "Detector Fault-Shorted Loop Fault";
                        break;
                    }

                case 88:
                    {
                        Desc = "Detetor Fault-Excessive Change Fault";
                        break;
                    }

                case 89:
                    {
                        Desc = "PedDetector Off";
                        break;
                    }

                case 90:
                    {
                        Desc = "PedDetector On";
                        break;
                    }

                case 91:
                    {
                        Desc = "Pedestrian Detector Failed";
                        break;
                    }

                case 92:
                    {
                        Desc = "Pedestrian Detector Restored";
                        break;
                    }

                case 101:
                    {
                        Desc = "Preempt Advance Warning Input";
                        break;
                    }

                case 102:
                    {
                        Desc = "Preempt (Call) Input On";
                        break;
                    }

                case 103:
                    {
                        Desc = "Preempt Gate Down Input Received";
                        break;
                    }

                case 104:
                    {
                        Desc = "Preempt (Call) Input Off";
                        break;
                    }

                case 105:
                    {
                        Desc = "Preempt Entry Started";
                        break;
                    }

                case 106:
                    {
                        Desc = "Preemption Begin Track Clearance";
                        break;
                    }

                case 107:
                    {
                        Desc = "Preemption Begin Dwell Service";
                        break;
                    }

                case 108:
                    {
                        Desc = "Preemption Link Active On";
                        break;
                    }

                case 109:
                    {
                        Desc = "Preemption Link Active Off";
                        break;
                    }

                case 110:
                    {
                        Desc = "Preemption Max Presence Exceeded";
                        break;
                    }

                case 111:
                    {
                        Desc = "Preemption Begin Exit Interval";
                        break;
                    }

                case 112:
                    {
                        Desc = "TSP Check In";
                        break;
                    }

                case 113:
                    {
                        Desc = "TSP Adjustment to Early Green";
                        break;
                    }

                case 114:
                    {
                        Desc = "TSP Adjustment to Extend Green";
                        break;
                    }

                case 115:
                    {
                        Desc = "TSP Check Out";
                        break;
                    }

                case 131:
                    {
                        Desc = "Coord Pattern Change";
                        break;
                    }

                case 132:
                    {
                        Desc = "Cycle Length Change";
                        break;
                    }

                case 133:
                    {
                        Desc = "Offset Length Change";
                        break;
                    }

                case 134:
                    {
                        Desc = "Split 1 Change";
                        break;
                    }

                case 135:
                    {
                        Desc = "Split 2 Change";
                        break;
                    }

                case 136:
                    {
                        Desc = "Split 3 Change";
                        break;
                    }

                case 137:
                    {
                        Desc = "Split 4 Change";
                        break;
                    }

                case 138:
                    {
                        Desc = "Split 5 Change";
                        break;
                    }

                case 139:
                    {
                        Desc = "Split 6 Change";
                        break;
                    }

                case 140:
                    {
                        Desc = "Split 7 Change";
                        break;
                    }

                case 141:
                    {
                        Desc = "Split 8 Change";
                        break;
                    }

                case 142:
                    {
                        Desc = "Split 9 Change";
                        break;
                    }

                case 143:
                    {
                        Desc = "Split 10 Change";
                        break;
                    }

                case 144:
                    {
                        Desc = "Split 11 Change";
                        break;
                    }

                case 145:
                    {
                        Desc = "Split 12 Change";
                        break;
                    }

                case 146:
                    {
                        Desc = "Split 13 Change";
                        break;
                    }

                case 147:
                    {
                        Desc = "Split 14 Change";
                        break;
                    }

                case 148:
                    {
                        Desc = "Split 15 Change";
                        break;
                    }

                case 149:
                    {
                        Desc = "Split 16 Change";
                        break;
                    }

                case 150:
                    {
                        Desc = "Coord cycle state change";
                        break;
                    }

                case 151:
                    {
                        Desc = "Coordinated phase yield point";
                        break;
                    }

                case 171:
                    {
                        Desc = "Test Input on";
                        break;
                    }

                case 172:
                    {
                        Desc = "Test Input off";
                        break;
                    }

                case 173:
                    {
                        Desc = "Unit Flash Status change";
                        break;
                    }

                case 174:
                    {
                        Desc = "Unit Alarm Status 1 change";
                        break;
                    }

                case 175:
                    {
                        Desc = "Alarm Group State Change";
                        break;
                    }

                case 176:
                    {
                        Desc = "Special Function Output on";
                        break;
                    }

                case 177:
                    {
                        Desc = "Special Function Output off";
                        break;
                    }

                case 178:
                    {
                        Desc = "Manual control enable off/on";
                        break;
                    }

                case 179:
                    {
                        Desc = "Interval Advance off/on";
                        break;
                    }

                case 180:
                    {
                        Desc = "Stop Time Input off/on";
                        break;
                    }

                case 181:
                    {
                        Desc = "Controller Clock Updated";
                        break;
                    }

                case 182:
                    {
                        Desc = "Power Failure Detected";
                        break;
                    }

                case 184:
                    {
                        Desc = "Power Restored";
                        break;
                    }

                case 185:
                    {
                        Desc = "Vendor Specific Alarm";
                        break;
                    }

                default:
                    {
                        Desc = "Unknown";
                        break;
                    }
            }
            return Desc;
        }
    }
}
