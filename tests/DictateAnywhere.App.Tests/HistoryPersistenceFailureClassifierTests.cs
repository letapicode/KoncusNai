using System.Security;
using DictateAnywhere.App.History;

namespace DictateAnywhere.App.Tests;

public sealed class HistoryPersistenceFailureClassifierTests
{
  [Xunit.Theory]
  [Xunit.InlineData(typeof(IOException))]
  [Xunit.InlineData(typeof(UnauthorizedAccessException))]
  [Xunit.InlineData(typeof(InvalidOperationException))]
  [Xunit.InlineData(typeof(SecurityException))]
  [Xunit.InlineData(typeof(NotSupportedException))]
  public void IsExpected_ClassifiesRecoverablePersistenceFailures(Type exceptionType)
  {
    Exception exception = (Exception)Activator.CreateInstance(exceptionType)!;

    Xunit.Assert.True(HistoryPersistenceFailureClassifier.IsExpected(exception));
  }

  [Xunit.Fact]
  public void IsExpected_DoesNotHideProgrammingFailures()
  {
    Xunit.Assert.False(HistoryPersistenceFailureClassifier.IsExpected(new ArgumentException("invalid query")));
  }
}
