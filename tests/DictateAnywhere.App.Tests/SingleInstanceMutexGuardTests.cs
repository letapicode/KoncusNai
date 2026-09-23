using System;

namespace DictateAnywhere.App.Tests;

public sealed class SingleInstanceMutexGuardTests
{
  [Xunit.Fact]
  public void TryAcquire_AllowsOnlyOneOwnerPerMutexName()
  {
    string mutexName = $@"Local\DictateAnywhere.Tests.{Guid.NewGuid():N}";

    using SingleInstanceMutexGuard first = new(mutexName);
    Xunit.Assert.True(first.TryAcquire());

    using SingleInstanceMutexGuard second = new(mutexName);
    Xunit.Assert.False(second.TryAcquire());
  }

  [Xunit.Fact]
  public void TryAcquire_AfterOwnerDisposes_NewOwnerCanAcquire()
  {
    string mutexName = $@"Local\DictateAnywhere.Tests.{Guid.NewGuid():N}";

    using (SingleInstanceMutexGuard first = new(mutexName))
    {
      Xunit.Assert.True(first.TryAcquire());
    }

    using SingleInstanceMutexGuard second = new(mutexName);
    Xunit.Assert.True(second.TryAcquire());
  }
}
