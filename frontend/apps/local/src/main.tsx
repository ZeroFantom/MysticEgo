import React from 'react';
import {createRoot} from 'react-dom/client';
import {MysticApp} from '@mysticego/ui';
import '@mysticego/ui/styles.css';
createRoot(document.getElementById('root')!).render(<React.StrictMode><MysticApp mode="local" title="Локальная панель" defaultApi="http://localhost:5200"/></React.StrictMode>);
