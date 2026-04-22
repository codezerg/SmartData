// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// https://astro.build/config
export default defineConfig({
	integrations: [
		starlight({
			title: 'SmartData',
			description: 'A .NET data framework with AutoRepo ORM, binary RPC, schema migration, and an embedded admin console.',
			social: [
				{ icon: 'github', label: 'GitHub', href: 'https://github.com/codezerg/SmartData' },
			],
			sidebar: [
				{
					label: 'Start Here',
					items: [
						{ label: 'Introduction', slug: 'index' },
						{ label: 'Install', slug: 'install' },
						{ label: 'Guide', slug: 'guide' },
					],
				},
				{
					label: 'Server',
					items: [
						{ label: 'SmartData.Server', slug: 'server/server' },
						{ label: 'Tracking & Ledger', slug: 'server/tracking' },
					],
				},
				{
					label: 'Providers',
					items: [
						{ label: 'SQLite', slug: 'providers/sqlite' },
						{ label: 'SQL Server', slug: 'providers/sqlserver' },
					],
				},
				{
					label: 'Client & Core',
					items: [
						{ label: 'SmartData.Client', slug: 'client/client' },
						{ label: 'SmartData.Core', slug: 'core/core' },
					],
				},
				{
					label: 'Tools',
					items: [
						{ label: 'CLI (sd.exe)', slug: 'tools/cli' },
						{ label: 'Admin Console', slug: 'tools/console' },
					],
				},
			],
		}),
	],
});
