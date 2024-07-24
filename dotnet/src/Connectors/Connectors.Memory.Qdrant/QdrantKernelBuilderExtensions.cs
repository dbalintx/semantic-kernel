﻿// Copyright (c) Microsoft. All rights reserved.

using Microsoft.SemanticKernel.Data;

namespace Microsoft.SemanticKernel.Connectors.Qdrant;

/// <summary>
/// Extension methods to register Qdrant <see cref="IVectorStore"/> instances on the <see cref="IKernelBuilder"/>.
/// </summary>
public static class QdrantKernelBuilderExtensions
{
    /// <summary>
    /// Register a Qdrant <see cref="IVectorStore"/> with the specified service ID.
    /// </summary>
    /// <param name="builder">The builder to register the <see cref="IVectorStore"/> on.</param>
    /// <param name="host">The Qdrant service host name.</param>
    /// <param name="port">The Qdrant service port.</param>
    /// <param name="https">A value indicating whether to use HTTPS for communicating with Qdrant.</param>
    /// <param name="apiKey">The Qdrant service API key.</param>
    /// <param name="serviceId">An optional service id to use as the service key.</param>
    /// <param name="options">Optional options to further configure the <see cref="IVectorStore"/>.</param>
    /// <returns>The kernel builder.</returns>
    public static IKernelBuilder AddQdrantVectorStore(this IKernelBuilder builder, string? host = default, int port = 6334, bool https = false, string? apiKey = default, string? serviceId = default, QdrantVectorStoreOptions? options = default)
    {
        builder.Services.AddQdrantVectorStore(host, port, https, apiKey, serviceId, options);
        return builder;
    }
}