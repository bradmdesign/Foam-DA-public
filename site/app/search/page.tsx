import {LibrarySearch} from '@/components/library-search';
export const metadata={title:'Search'};
export default function SearchPage(){return <main id="main" className="page-container"><nav className="breadcrumbs" aria-label="Breadcrumb"><a href="/">Library</a><span>/</span><span>Search</span></nav><header className="catalog-intro"><div className="eyebrow">The local collection</div><h1>Search the library</h1><p>Quest instructions, item aliases, class requirements, and places.</p></header><LibrarySearch/></main>}
