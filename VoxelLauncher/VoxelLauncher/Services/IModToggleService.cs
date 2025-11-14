using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VoxelLauncher.ViewModels;

namespace VoxelLauncher.Services
{
    public interface IModToggleService
    {
        Task ToggleModAsync(ModrinthMod mod);
    }
}
