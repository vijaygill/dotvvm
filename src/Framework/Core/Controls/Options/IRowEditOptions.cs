﻿namespace DotVVM.Framework.Controls
{
    /// <summary>
    /// Represents settings for the row (item) edit feature.
    /// </summary>
    public interface IRowEditOptions
    {
        /// <summary>
        /// Gets or sets the name of a property that uniquely identifies a row. (row ID, primary key, etc.). The value may be left out if inline editing is not enabled.
        /// </summary>
        string? PrimaryKeyPropertyName { get; }

        /// <summary>
        /// Gets or sets the value of a <see cref="PrimaryKeyPropertyName"/> property for the row that is being edited. Null if nothing is edited.
        /// </summary>
        object? EditRowId { get; }
    }
}
