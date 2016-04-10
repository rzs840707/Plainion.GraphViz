﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Plainion.GraphViz.Modules.CodeInspection.Packaging.Spec;

namespace Plainion.GraphViz.Modules.CodeInspection.Packaging.Services
{
    class PackageAnalyzer
    {
        private static string[] Colors = { "LightBlue", "LightGreen", "LightGray", "LightCoral", "Brown" };

        private SystemPackaging myConfig;
        private CancellationToken myCancellationToken;
        private readonly AssemblyLoader myAssemblyLoader;
        private Dictionary<string, List<Type>> myPackageToTypesMap;
        private List<Package> myRelevantPackages;

        public PackageAnalyzer()
        {
            myAssemblyLoader = new AssemblyLoader();
            PackagesToAnalyze = new List<string>();
        }

        /// <summary>
        /// If empty the dependencies between all packages will be analyzed
        /// </summary>
        public IList<string> PackagesToAnalyze { get; private set; }

        public bool UsedTypesOnly { get; set; }

        public AnalysisDocument Execute( SystemPackaging config, CancellationToken cancellationToken )
        {
            myConfig = config;
            myCancellationToken = cancellationToken;

            myRelevantPackages = PackagesToAnalyze.Any()
                ? myConfig.Packages
                    .Where( p => PackagesToAnalyze.Any( name => p.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) )
                    .ToList()
                : myConfig.Packages;

            myPackageToTypesMap = new Dictionary<string, List<Type>>();

            Load();

            Console.WriteLine( "Analyzing ..." );

            var edges = Analyze()
                .Distinct()
                .ToList();

            Console.WriteLine();

            if( myAssemblyLoader.SkippedAssemblies.Any() )
            {
                Console.WriteLine( "Skipped assemblies:" );
                foreach( var asm in myAssemblyLoader.SkippedAssemblies )
                {
                    Console.WriteLine( "  {0}", asm );
                }
                Console.WriteLine();
            }

            Console.WriteLine( "Building Graph ..." );

            return GenerateDocument( edges );
        }

        private void Load()
        {
            foreach( var package in myRelevantPackages )
            {
                myCancellationToken.ThrowIfCancellationRequested();

                myPackageToTypesMap[ package.Name ] = Load( package )
                    .SelectMany( asm => asm.GetTypes() )
                    .ToList();
            }
        }

        private IEnumerable<Assembly> Load( Package package )
        {
            Console.WriteLine( "Assembly root {0}", Path.GetFullPath( myConfig.AssemblyRoot ) );
            Console.WriteLine( "Loading package {0}", package.Name );

            return package.Includes
                .SelectMany( i => Directory.GetFiles( myConfig.AssemblyRoot, i.Pattern ) )
                .Where( file => !package.Excludes.Any( e => e.Matches( file ) ) )
                .Select( myAssemblyLoader.Load )
                .Where( asm => asm != null )
                .ToList();
        }

        private Edge[] Analyze()
        {
            return myRelevantPackages
                .SelectMany( p => myPackageToTypesMap[ p.Name ]
                    .Select( t => new
                    {
                        Package = p,
                        Type = t
                    } )
                )
                .AsParallel()
                .WithCancellation( myCancellationToken )
                .SelectMany( e => Analyze( e.Package, e.Type ) )
                .ToArray();
        }

        private IEnumerable<Edge> Analyze( Package package, Type type )
        {
            Console.Write( "." );

            myCancellationToken.ThrowIfCancellationRequested();

            var focusedPackageTypes = myPackageToTypesMap.Count > 1
                ? null
                : myPackageToTypesMap.Single().Value;

            return new Reflector( myAssemblyLoader, type ).GetUsedTypes()
                // if only one package is given we analyse the deps within the package - otherwise between the packages
                .Where( edge => focusedPackageTypes != null ? focusedPackageTypes.Contains( edge.Target ) : IsForeignPackage( package, edge.Target ) )
                .Select( edge => GraphUtils.Edge( edge ) )
                .Where( edge => edge.Source != edge.Target );
        }

        private bool IsForeignPackage( Package package, Type dep )
        {
            return myPackageToTypesMap.Where( e => e.Key != package.Name ).Any( entry => entry.Value.Contains( dep ) );
        }

        private AnalysisDocument GenerateDocument( IReadOnlyCollection<Edge> edges )
        {
            var doc = new AnalysisDocument();

            var nodesWithEdgesIndex = new HashSet<Type>();
            if( UsedTypesOnly )
            {
                foreach( var edge in edges )
                {
                    nodesWithEdgesIndex.Add( edge.Source );
                    nodesWithEdgesIndex.Add( edge.Target );
                }
            }

            var relevantNotesWithCluster = myPackageToTypesMap
                .Select( e => new
                {
                    Package = myRelevantPackages.Single( p => p.Name == e.Key ),
                    Types = e.Value
                } )
                .SelectMany( ( e, idx ) => e.Types
                    .Select( t => new
                    {
                        Type = t,
                        Package = e.Package,
                        PackageIndex = idx
                    } ) )
                .AsParallel()
                .Where( e => !UsedTypesOnly || nodesWithEdgesIndex.Contains( e.Type ) )
                .Select( e => new
                {
                    Node = GraphUtils.Node( e.Type ),
                    Cluster = e.Package.Clusters.FirstOrDefault( c => c.Matches( e.Type.FullName ) ),
                    PackageIndex = e.PackageIndex
                } );

            foreach( var entry in relevantNotesWithCluster )
            {
                doc.Add( entry.Node );

                if( entry.Cluster != null )
                {
                    doc.AddToCluster( entry.Node, entry.Cluster );
                }

                if( myPackageToTypesMap.Count > 1 )
                {
                    // color coding of nodes we only need if multiple packages were analyzed
                    doc.AddNodeColor( entry.Node, Colors[ entry.PackageIndex % Colors.Length ] );
                }
            }

            foreach( var edge in edges )
            {
                doc.Add( edge );

                var color = GetEdgeColor( edge );
                if( color != null )
                {
                    doc.AddEdgeColor( edge, color );
                }
            }

            return doc;
        }

        private static string GetEdgeColor( Edge edge )
        {
            if( edge.EdgeType == EdgeType.DerivesFrom || edge.EdgeType == EdgeType.Implements )
            {
                return "Blue";
            }
            else if( edge.EdgeType != EdgeType.Calls )
            {
                return "Gray";
            }
            return null;
        }
    }
}
