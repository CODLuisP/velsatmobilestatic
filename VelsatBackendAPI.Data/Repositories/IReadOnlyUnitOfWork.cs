using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VelsatBackendAPI.Data.Repositories;

namespace VelsatMobile.Data.Repositories
{
    public interface IReadOnlyUnitOfWork : IDisposable
    {
        IAplicativoRepository AplicativoRepository { get; }

        IUserRepository UserRepository { get; }

    }
}
