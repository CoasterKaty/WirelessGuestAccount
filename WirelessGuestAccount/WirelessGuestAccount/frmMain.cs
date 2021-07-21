/* frmMain
 * Main form for Wireless Guest Manager
 * Katy Nicholson
 * https://katystech.blog/
 */
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.IO.Ports;

namespace WirelessGuestAccount
{
    public partial class frmMain : Form
    {
        Settings kSettings;
        public frmMain()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            cboValidity.SelectedIndex = 1;
            kSettings = new Settings(true, "config.xml", AppDomain.CurrentDomain.BaseDirectory);
            lblInfo.Text = kSettings.Get("LabelText", "Guests must have a wireless pass in order to access the guest wireless. Passes are valid for 1, 2, 5 or 30 days, or unlimited.\n\"Guest Details\" is not printed on the ticket. This is optional for short length passes but required for 30 day or unlimited passes.\nInstructions for connecting will be on the printed ticket, they will need to use the $SSID$ network and the supplied username/password.\nStaff and students should use the $SSID$ network with their existing school login details and should not be using guest logins.");
            lblInfo.Text = lblInfo.Text.Replace("$SSID$", kSettings.Get("SSID", "SSID"));
        }

        private bool checkUserExists(PrincipalContext namingContext, string strUsername)
        {
            using (var user = UserPrincipal.FindByIdentity(namingContext, strUsername))
            {
                if (user != null)
                {
                    user.Dispose();
                    return true;
                }
            }
            return false;
        }
        private void printTicket(SerialPort port, string strUsername, string strPassword, DateTime dtValidity, bool bolExpires)
        {
            DateTime dtExpires = dtValidity.AddDays(-1);
            
            ASCIIEncoding ascii = new ASCIIEncoding();
            port.Write(ascii.GetString(new byte[] { 27, 97, 1 }));
            // Print the logo in ID 1
            port.Write(ascii.GetString(new byte[] { 28, 112, 1, 0 }));
            port.WriteLine("");
            port.Write(ascii.GetString(new byte[] { 27, 82, 3 }));
            port.Write(ascii.GetString(new byte[] { 27, 33, 185 }));
            port.WriteLine("Guest Wireless Details");
            port.WriteLine("");
           
            port.Write(ascii.GetString(new byte[] { 27, 33, 0 }));
         // aaaaaaaaaaaaaaa123456789012345678901234567890123456789012
            port.WriteLine("Connect your device to the " + kSettings.Get("SSID", "SSID"));
            port.WriteLine("network using the following details:");
            port.WriteLine("");
            port.WriteLine("Username: " + strUsername);
            port.WriteLine("Password: " + strPassword);
            port.WriteLine("");
            port.WriteLine("Apple Devices: Press Trust when prompted");
            port.WriteLine("for certificate.");
            port.WriteLine("");
            port.WriteLine("Other Devices: Select these if prompted:");
            port.WriteLine("EAP Method: PEAP");
            port.WriteLine("CA Certificate: Don't Validate");
            port.WriteLine("Identity: " + strUsername);
            port.WriteLine("Anonymous Identity: (leave blank)");
            port.WriteLine("");
            if (bolExpires)
            {
                port.WriteLine("This pass is valid until the end of:");
                port.WriteLine(dtExpires.ToShortDateString());
            }
            else
            {
                port.WriteLine("This pass does not expire.");
            }


            port.WriteLine("");
            port.Write(ascii.GetString(new byte[] { 29, 86, 65, 0 }));
        }
        private void cmdPrint_Click(object sender, EventArgs e)
        {
            cmdPrint.Enabled = false;
            cmdPrint.Text = "Printing...";
            try
            {
                using (var namingContext = new PrincipalContext(ContextType.Domain, null, kSettings.Get("DN", "ou=Guest Wireless,DC=contoso,DC=com")))
                {

                    using (SerialPort port = new SerialPort(kSettings.Get("Port", "COM7"), 19200, Parity.None, 8, StopBits.One))
                    {
                        port.Open();
                        DateTime dtExpires = DateTime.Now;
                        bool bolExpires = true;
                        switch (cboValidity.SelectedIndex)
                        {
                            case 0:
                                //1 day
                                dtExpires = DateTime.Now.AddDays(2);
                                break;
                            case 1:
                                // 2 days
                                dtExpires = DateTime.Now.AddDays(3);
                                break;
                            case 2:
                                // 5 days
                                dtExpires = DateTime.Now.AddDays(6);
                                break;
                            case 3:
                                //30 days
                                dtExpires = DateTime.Now.AddDays(31);
                                break;
                            case 4:
                                //unlimited
                                bolExpires = false;
                                break;

                        }
                        using (GroupPrincipal grpWifi = GroupPrincipal.FindByIdentity(namingContext, kSettings.Get("Group", "G GuestWireless")))
                        {
                            for (int intCount = 1; intCount <= numPassCount.Value; intCount++)
                            {
                                // Generate accounts
                                bool bolUserExists = true;
                                string strUsername = "";
                                string strPassword = "";
                                // Username is guest-xxxx where xxxx are random digits, obtained by generating a GUID and picking 4 digits.
                                while (bolUserExists)
                                {
                                    string strAccountGuid = Guid.NewGuid().ToString();
                                    strUsername = "guest-" + strAccountGuid.Substring(0, 4);
                                    strPassword = strAccountGuid.Substring(24, 6);
                                    bolUserExists = checkUserExists(namingContext, strUsername);
                                }

                                //Create user

                                using (var newUser = new UserPrincipal(namingContext))
                                {
                                    newUser.SamAccountName = strUsername;
                                    newUser.UserPrincipalName = strUsername + kSettings.Get("UPNSuffix", "@contoso.com");
                                    newUser.Name = strUsername;
                                    if (txtDetails.Text.Length > 0) newUser.Description = txtDetails.Text;
                                    newUser.Enabled = true;
                                    newUser.SetPassword(strPassword);
                                    if (bolExpires) newUser.AccountExpirationDate = dtExpires;
                                    newUser.PermittedWorkstations.Add(kSettings.Get("Server", "CONTOSOSERVER1"));
                                    newUser.Save();
                                    grpWifi.Members.Add(namingContext, IdentityType.UserPrincipalName, strUsername + kSettings.Get("UPNSuffix", "@contoso.com"));
                                }
                                // Print tickets
                                printTicket(port, strUsername, strPassword, dtExpires, bolExpires);
                            }
                            grpWifi.Save();
                        }
                        port.Close();
                    }
                }
            }
            catch (Exception dsException)
            {
                MessageBox.Show(dsException.Message, "Error",MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            cmdPrint.Text = "Print Passes";
            cmdPrint.Enabled = true;
        }


        private void cmdReset_Click(object sender, EventArgs e)
        {
            txtDetails.Text = "";
            cboValidity.SelectedIndex = 1;
            numPassCount.Value = 1;
            txtDetails.Focus();

        }

        private void lblInfo_Click(object sender, EventArgs e)
        {

        }
    }
}
