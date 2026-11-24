import React from 'react';
import {createRoot} from 'react-dom/client';
import {MysticApp} from '@mysticego/ui';
import '@mysticego/ui/styles.css';
createRoot(document.getElementById('root')!).render(<React.StrictMode><MysticApp mode="orchestra" title="Orchestra · центр разработки" defaultApi="http://127.0.0.1:5500"/></React.StrictMode>);
