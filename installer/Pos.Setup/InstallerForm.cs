using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Drawing;

namespace Pos.Setup;

public sealed class InstallerForm : Form
{
    readonly bool uninstall; readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Punto de Venta");
    readonly Label status = new(); readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100 }; readonly Button run = new();
    readonly CheckBox terms = new() { Text = "Acepto los términos y condiciones", AutoSize = true, Checked = false };
    readonly TextBox admin = new() { Text = "Administrador" }; readonly TextBox password = new() { Text = "12345" }; readonly TextBox store = new() { Text = "Mi tienda" };
    readonly CheckBox desktop = new() { Text = "Acceso directo en el escritorio", AutoSize = true, Checked = true }; readonly CheckBox start = new() { Text = "Menú Inicio", AutoSize = true, Checked = true };

    public InstallerForm(bool isUninstall)
    {
        uninstall = isUninstall; Text = isUninstall ? "Desinstalar Punto de Venta" : "Instalación de Punto de Venta"; ClientSize = new Size(720, 600); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Controls.Add(new Label { Text = Text, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Color.FromArgb(23,50,77), Location = new Point(28,25), AutoSize = true });
        Controls.Add(new Label { Text = "Punto de Venta - instalador para Windows x64", Location = new Point(30,75), AutoSize = true, ForeColor = Color.FromArgb(82,101,119) });
        if (isUninstall) Controls.Add(new Label { Text = "Se quitarán la aplicación y los servicios. La base de datos y respaldos se conservarán.", Location = new Point(30,140), Size = new Size(650,60) });
        else { Controls.Add(new Label { Text = "Dependencias: PostgreSQL incluido, API local, cliente WPF y Microsoft Visual C++ Redistributable.", Location = new Point(30,130), Size = new Size(650,45) }); terms.SetBounds(30,190,400,25); Controls.Add(terms); Add("Administrador",admin,30,240); Add("Contraseña",password,250,240); Add("Nombre de tienda",store,470,240); desktop.SetBounds(30,325,260,25); start.SetBounds(310,325,150,25); Controls.Add(desktop); Controls.Add(start); Controls.Add(new Label { Text = "Carpeta de instalación: " + root, Location = new Point(30,370), Size = new Size(650,30) }); }
        status.SetBounds(30,430,650,45); bar.SetBounds(30,485,650,24); run.Text = isUninstall ? "Desinstalar" : "Instalar"; run.SetBounds(540,530,140,36); run.Click += async (_,_) => await Execute(); Controls.Add(status); Controls.Add(bar); Controls.Add(run);
    }
    void Add(string label, TextBox box, int x, int y) { Controls.Add(new Label { Text = label, Location = new Point(x,y), AutoSize = true }); box.SetBounds(x,y+22,200,30); Controls.Add(box); }
    async Task Execute()
    {
        if (!uninstall && !terms.Checked) { status.Text = "Debes aceptar los términos y condiciones."; return; } run.Enabled=false;
        try { if (uninstall) { await Ps(Path.Combine(root,"install-production.ps1"),"-Uninstall"); Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PuntoDeVenta",false); Set(100,"Desinstalación terminada. Tus datos se conservaron."); }
            else { var temp=Path.Combine(Path.GetTempPath(),"PuntoDeVenta-Setup",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); try { await Extract(temp); await Copy(temp); } finally { try{Directory.Delete(temp,true);}catch{} } Set(76,"Instalando Microsoft Visual C++..."); await Run(Path.Combine(root,"vc_redist.x64.exe"),"/install /quiet /norestart"); Set(82,"Configurando PostgreSQL y la API..."); await Ps(Path.Combine(root,"install-production.ps1"),""); await SetupAdmin(); Register(); MakeLinks(); Set(100,"Instalación terminada correctamente."); } }
        catch(Exception ex) { status.Text="Error: "+ex.Message; Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"PuntoDeVenta","logs")); File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"PuntoDeVenta","logs","setup-error.log"),ex+Environment.NewLine); run.Enabled=true; }
    }
    async Task Extract(string dest) { await using var s=typeof(InstallerForm).Assembly.GetManifestResourceStream("PuntoDeVenta.Payload.zip")??throw new InvalidOperationException("No se encontró el paquete interno."); using var z=new ZipArchive(s); var total=z.Entries.Sum(e=>Math.Max(0,e.Length)); long done=0; foreach(var e in z.Entries){var t=Path.GetFullPath(Path.Combine(dest,e.FullName.Replace('/',Path.DirectorySeparatorChar)));if(!t.StartsWith(Path.GetFullPath(dest)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Paquete inválido.");if(e.Name.Length==0){Directory.CreateDirectory(t);continue;}Directory.CreateDirectory(Path.GetDirectoryName(t)!);await using var i=e.Open();await using var o=File.Create(t);var b=new byte[65536];int n;while((n=await i.ReadAsync(b))>0){await o.WriteAsync(b.AsMemory(0,n));done+=n;Set(3+(int)(done*55/Math.Max(1,total)),"Copiando: "+e.FullName);}} }
    async Task Copy(string src){var f=Directory.GetFiles(src,"*",SearchOption.AllDirectories);for(int n=0;n<f.Length;n++){var rel=Path.GetRelativePath(src,f[n]);var t=Path.Combine(root,rel);Directory.CreateDirectory(Path.GetDirectoryName(t)!);File.Copy(f[n],t,true);Set(58+n*15/Math.Max(1,f.Length),"Instalando: "+rel);await Task.Yield();}}
    async Task Ps(string script,string extra)=>await Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),$"-NoProfile -ExecutionPolicy Bypass -File {Program.QuoteArgument(script)} -InstallRoot {Program.QuoteArgument(root)} {extra}");
    async Task Run(string file,string args){using var p=Process.Start(new ProcessStartInfo(file,args){WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true})??throw new InvalidOperationException("No se pudo iniciar "+Path.GetFileName(file));p.OutputDataReceived+=(_,e)=>{if(!string.IsNullOrWhiteSpace(e.Data))BeginInvoke(()=>status.Text=e.Data);};p.BeginOutputReadLine();p.BeginErrorReadLine();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new InvalidOperationException(Path.GetFileName(file)+" terminó con código "+p.ExitCode);}
    async Task SetupAdmin(){using var c=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:5000"),Timeout=TimeSpan.FromSeconds(10)};for(int n=0;n<20;n++){try{var s=await c.GetFromJsonAsync<SetupStatus>("/api/setup/status");if(s?.Configured==true)return;if(s is not null){var r=await c.PostAsJsonAsync("/api/setup/initial",new{storeName=store.Text,businessType="Comercio general",userName="admin",password=password.Text,administratorName=admin.Text,registerName="Caja 1"});if(!r.IsSuccessStatusCode&&r.StatusCode!=System.Net.HttpStatusCode.Conflict)throw new InvalidOperationException("No se pudo crear el administrador.");return;}}catch(HttpRequestException){await Task.Delay(1000);}}throw new InvalidOperationException("La API no respondió.");}
    void Register(){using var k=Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PuntoDeVenta");k?.SetValue("DisplayName","Punto de Venta");k?.SetValue("DisplayVersion","2.1.4");k?.SetValue("InstallLocation",root);k?.SetValue("UninstallString",Program.QuoteArgument(Path.Combine(root,"Setup.exe"))+" /uninstall");}
    void MakeLinks(){var target=Path.Combine(root,"client","Pos.Desktop.exe");var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);if(shell is null)return;var t=shell.GetType();void Link(string p){Directory.CreateDirectory(Path.GetDirectoryName(p)!);var x=t.InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{p});x!.GetType().InvokeMember("TargetPath",System.Reflection.BindingFlags.SetProperty,null,x,new object[]{target});x.GetType().InvokeMember("Save",System.Reflection.BindingFlags.InvokeMethod,null,x,null);}if(desktop.Checked)Link(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Punto de Venta.lnk"));if(start.Checked)Link(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),"Punto de Venta.lnk"));}
    void Set(int n,string text){if(!IsDisposed)BeginInvoke(()=>{bar.Value=Math.Clamp(n,0,100);status.Text=text;});}
    sealed record SetupStatus(bool Configured,string? StoreName);
}
