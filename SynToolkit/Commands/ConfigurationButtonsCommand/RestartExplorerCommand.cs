using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Utils;

namespace SynToolkit.Commands
{
    public class RestartExplorerCommand : AsyncCommandBase
    {
        protected override async Task ExecuteAsync(object parameter)
        {
            await Task.Run(() => 
            {
                ProcessHelper.KillProcessByName("explorer.exe");
            });
        }
    }
}
