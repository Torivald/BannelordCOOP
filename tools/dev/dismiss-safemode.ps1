<#
.SYNOPSIS
  Answers "No" to Bannerlord's "Safe Mode" MessageBox (shown after the previous run was killed):
  posts WM_COMMAND/IDNO to the dialog window. Prints how many dialogs were dismissed.
#>
Add-Type -Namespace Win -Name Api -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);'
$n = 0
foreach ($p in Get-Process Bannerlord -ErrorAction SilentlyContinue) {
    if ($p.MainWindowTitle -ne 'Safe Mode') { continue }
    # WM_COMMAND = 0x111, IDNO = 7
    if ([Win.Api]::PostMessage($p.MainWindowHandle, 0x111, [System.IntPtr]7, [System.IntPtr]::Zero)) { $n++ }
}
Write-Output $n
