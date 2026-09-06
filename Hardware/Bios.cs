  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/

using System;
using System.IO;
using System.Text;
using Microsoft.Management.Infrastructure;
using OmenMon.Library;

namespace OmenMon.Hardware.Bios {

    // Opt-in BIOS/WMI call tracer. Enabled by setting the environment variable
    // OMENMON_BIOSTRACE to any non-empty value; writes one line per Bios.Send()
    // call (command, commandType, inData, return code, outData) to
    // OmenMon-biostrace.log next to the executable (or %TEMP% as a fallback).
    // Added to diagnose boards where fan WMI calls silently no-op (8BCA / 16-xf0xxx).
    internal static class BiosTrace {
        private static readonly bool enabled =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OMENMON_BIOSTRACE"));
        private static readonly object gate = new object();
        private static string path;

        public static bool Enabled { get { return enabled; } }

        private static string Path() {
            if(path != null) return path;
            try {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string p = System.IO.Path.Combine(dir, "OmenMon-biostrace.log");
                File.AppendAllText(p, ""); // probe writability
                path = p;
            } catch {
                path = System.IO.Path.Combine(
                    Environment.GetEnvironmentVariable("TEMP") ?? ".", "OmenMon-biostrace.log");
            }
            return path;
        }

        private static string Hex(byte[] b) {
            if(b == null) return "(null)";
            if(b.Length == 0) return "(empty)";
            var sb = new StringBuilder(b.Length * 3);
            int n = b.Length > 32 ? 32 : b.Length;
            for(int i = 0; i < n; i++) sb.Append(b[i].ToString("X2")).Append(' ');
            if(b.Length > n) sb.Append("... (").Append(b.Length).Append(" bytes)");
            return sb.ToString().TrimEnd();
        }

        public static void Log(uint command, uint commandType, byte[] inData,
                               byte outDataSize, int returnCode, byte[] outData) {
            if(!enabled) return;
            try {
                lock(gate) {
                    File.AppendAllText(Path(), string.Format(
                        "{0:yyyy-MM-dd HH:mm:ss.fff}  cmd=0x{1:X}  type=0x{2:X2}  in=[{3}]  outSize={4}  rc={5}  out=[{6}]{7}",
                        DateTime.Now, command, commandType, Hex(inData), outDataSize,
                        returnCode, Hex(outData),
                        (returnCode == 1 || returnCode == 4 || returnCode == 6 || returnCode == 46)
                            ? "   <-- SWALLOWED by Check()" : "")
                        + Environment.NewLine);
                }
            } catch { }
        }
    }

#region Interface
    // Defines an interface for interacting with the BIOS
    public interface IBios : IDisposable {

        public bool IsInitialized { get; }

        public void Initialize();
        public void Close();

        // Read and write
        public int Send(
            BiosData.Cmd command,
            uint commandType,
            byte[] inData,
            byte outDataSize,
            out byte[] outData);

        // Write only
        public int Send(
            BiosData.Cmd command,
            uint commandType,
            byte[] inData);

    }
#endregion

    // Provides for BIOS call error handling
    public class BiosException : Exception { 

        public BiosException(string message) : base(message) { }

    }

    // Implements the functionality for making BIOS calls via CIM (WMI)
    // Builds up on the BIOS data values and structures defined earlier
    public class Bios : BiosData, IBios {

#region Constants & Variables
        public bool IsInitialized { get; protected set; }

        private CimSession session;
        private CimInstance biosData, biosMethods;
#endregion

#region Initialization & Disposal
        // The following three statements ensure the class can be instantiated only once
        private static readonly Bios instance = new Bios();

        protected Bios() { }

        public static Bios Instance {
            get { return instance; }
        }

        // Sets up the CIM session and objects for subsequent WMI calls to the BIOS
        public void Initialize() {
            if(!this.IsInitialized) {
                try {

                    // Establish a new CIM session
                    this.session = CimSession.Create(null);

                    // Set up the BIOS data structure and pre-populate it with the shared secret
                    this.biosData = new CimInstance(this.session.GetClass(BIOS_NAMESPACE, BIOS_DATA));
                    this.biosData.CimInstanceProperties["Sign"].Value = Sign;

                    // Retrieve the BIOS methods instance
                    this.biosMethods = new CimInstance(BIOS_METHOD_CLASS, BIOS_NAMESPACE);
                    this.biosMethods.CimInstanceProperties.Add(CimProperty.Create("InstanceName", BIOS_METHOD_INSTANCE, CimFlags.Key));
                    this.biosMethods = session.GetInstance(BIOS_NAMESPACE, this.biosMethods);

                    // Alternatively, using System.Linq:
                    //this.biosMethods = this.session.QueryInstances("root\\wmi", "WQL", "SELECT * FROM hpqBIntM").SingleOrDefault();
		    
                    this.IsInitialized = true;
                 } catch {
                 }
            }
        }

        // Closes the CIM session and frees up the resources allocated to the CIM objects
        public void Close() {
            if(this.IsInitialized) {
                this.IsInitialized = false;
                try {
                    this.biosData.Dispose();
                    this.biosMethods.Dispose();
                    this.session.Dispose();
                } catch {
                }
            }
        }

        // Dispose() is just a wrapper for Close()
        public void Dispose() {
            Close();
        }
#endregion

        // Sends a command to the BIOS
        public int Send(
            BiosData.Cmd command,
            uint commandType,
            byte[] inData,
            byte outDataSize, // One of 0, 4, 128, 1024, or 4096 only
            out byte[] outData) {

            // Initialize the output variable
            outData = new byte[outDataSize];

            try {
                using(CimInstance input = new CimInstance(biosData)) {

                    // Define the input arguments for the request
                    input.CimInstanceProperties["Command"].Value = command;
                    input.CimInstanceProperties["CommandType"].Value = commandType;

                    if(inData == null) {

                        // Allow for a call with no data payload
                        input.CimInstanceProperties["Size"].Value = 0;

                    } else {

                        input.CimInstanceProperties[BIOS_DATA_FIELD].Value = inData;
                        input.CimInstanceProperties["Size"].Value = inData.Length;

                    }

                    // Prepare the method parameters
                    CimMethodParametersCollection methodParams = new();
                    methodParams.Add(CimMethodParameter.Create("InData", input, CimType.Instance, CimFlags.In));

                    // Call the pertinent method depending on the data size
                    CimMethodResult result = this.session.InvokeMethod(
                        this.biosMethods, BIOS_METHOD + Convert.ToString(outDataSize), methodParams);

                    // Retrieve the resulting data
                    using(CimInstance resultData = result.OutParameters["OutData"].Value as CimInstance) {

                        // Clean up
                        input.Dispose();
                        methodParams.Dispose();
                        result.Dispose();

                        // Populate the output data variable
                        if(outDataSize != 0)
                            outData = resultData.CimInstanceProperties["Data"].Value as byte[];

                        // Return the status code
                        int rc = Convert.ToInt32(resultData.CimInstanceProperties[BIOS_RETURN_CODE_FIELD].Value);
                        if(BiosTrace.Enabled)
                            BiosTrace.Log((uint) command, commandType, inData, outDataSize, rc, outData);
                        return rc;

                    }

                }

            } catch(Exception e) {

                // Return negative status code
                // for client-side exceptions
                if(BiosTrace.Enabled)
                    BiosTrace.Log((uint) command, commandType, inData, outDataSize, -1,
                        Encoding.ASCII.GetBytes("EXC: " + e.Message));
                return -1;
            }

        }

        // Wrapper for sending a BIOS command in case there is nothing to be sent as input
        public int Send(
            BiosData.Cmd command,
            uint commandType,
            byte[] inData) {

            byte[] outData = new byte[0];
            return Send(command, commandType, inData, 0, out outData);

        }

        // Evaluates the return status following a Send() call
        public void Check(int code, bool force = false) {

            // Optionally skip to make the application
            // usable with not fully-compatible models
            if(!force && !Config.BiosErrorReporting)
                return;

            // Check the return status
            switch(code) {
                case 0:
                    break;

                case -1: // Client-side exception
                    throw new BiosException(Config.GetError("ErrBiosCall|ErrBiosSend"));

                case 3: // Command not available
                    throw new BiosException(Config.GetError("ErrBiosCall|ErrBiosSendCommand"));

                case 5: // Insufficient input or output buffer size
                    throw new BiosException(Config.GetError("ErrBiosCall|ErrBiosSendSize"));

                // Codes 1, 4, 6 and 46 have been observed in the wild on
                // hardware that doesn't fully implement the HP Omen WMI
                // contract (e.g. Omen Transcend 14 fb0118TX returns 4 on
                // startup BIOS calls and crashes the GUI with "Unknown
                // response from BIOS: 4"). The exact semantics aren't
                // documented by HP, but every reported instance so far has
                // been a benign "this call isn't supported on this platform"
                // rather than a real error. Unlike code 3 — which the
                // original author chose to escalate to a hard BiosException
                // even when error reporting is on — these codes are silently
                // ignored regardless of Config.BiosErrorReporting / force,
                // because raising any of them on the Transcend 14 startup
                // path tears down the whole GUI before the user can see any
                // working readouts. Soft-failing here lets the rest of the
                // application come up and lets the user inspect what does
                // work on non-Omen-branded hardware.
                case 1:
                case 4:
                case 6:
                case 46:
                    return;

                default: // Unknown error
                    throw new BiosException(String.Format(Config.GetError("ErrBiosCall|ErrBiosSendUnknown"), code));

            }

        }

    }

}
