import React from 'react';
import {createRoot} from 'react-dom/client';
import {MysticApp} from '@mysticego/ui';
import '@mysticego/ui/styles.css';
createRoot(document.getElementById('root')!).render(<React.StrictMode><MysticApp mode="global" title="Глобальная панель" defaultApi="http://localhost:5100"/></React.StrictMode>);
