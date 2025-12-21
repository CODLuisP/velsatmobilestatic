using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VelsatMobile.Data.Repositories;

namespace VelsatBackendAPI.Data.Repositories
{
    public interface IUnitOfWork
    {

        IAplicativoRepository AplicativoRepository { get; }

        IUserRepository UserRepository { get; }

        void SaveChanges();

    }
}
